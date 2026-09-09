using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodexBridgeConsole
{
    public sealed class FrontMatterFile
    {
        private const string Delimiter = "---";

        // UTF8Encoding.GetPreamble() は encoderShouldEmitUTF8Identifier が false のとき空を返す。
        // 復号時に BOM を自前で取り除くため false のままにし、書き戻す BOM はこの定数で持つ。
        private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

        private readonly string _path;
        private readonly bool _hasUtf8Bom;
        private readonly string _newline;
        private readonly List<TextLine> _lines;
        private string _originalText;
        private int _closingDelimiterIndex;

        public FrontMatterFile(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            _path = Path.GetFullPath(path);

            byte[] bytes = File.ReadAllBytes(_path);
            _hasUtf8Bom = HasUtf8Bom(bytes);
            string text = DecodeUtf8(bytes, _hasUtf8Bom);
            _lines = SplitLines(text);

            if (_lines.Count == 0 || _lines[0].Content != Delimiter)
            {
                throw new InvalidDataException("ファイルの1行目がフロントマターの開始記号ではない");
            }

            _closingDelimiterIndex = FindClosingDelimiter(_lines);
            if (_closingDelimiterIndex < 0)
            {
                throw new InvalidDataException("フロントマターの終了記号が見つからない");
            }

            _newline = FindFirstNewline(_lines) ?? "\n";
            _originalText = text;
        }

        public string FilePath
        {
            get { return _path; }
        }

        public bool HasChanges
        {
            get { return !string.Equals(BuildText(), _originalText, StringComparison.Ordinal); }
        }

        public static FrontMatterFile Load(string path)
        {
            return new FrontMatterFile(path);
        }

        public string GetValue(string key)
        {
            string value;
            return TryGetValue(key, out value) ? value : null;
        }

        public bool TryGetValue(string key, out string value)
        {
            ValidateKey(key);

            int lineIndex = FindKeyLine(key);
            if (lineIndex < 0)
            {
                value = null;
                return false;
            }

            value = ParseValue(_lines[lineIndex].Content, key);
            return true;
        }

        public void SetValue(string key, string value)
        {
            ValidateKey(key);
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
            {
                throw new ArgumentException("値に改行を含めることはできない", nameof(value));
            }

            int lineIndex = FindKeyLine(key);
            if (lineIndex < 0)
            {
                _lines.Insert(_closingDelimiterIndex, new TextLine(key + ": " + value, _newline));
                _closingDelimiterIndex++;
                return;
            }

            TextLine line = _lines[lineIndex];
            if (string.Equals(ParseValue(line.Content, key), value, StringComparison.Ordinal))
            {
                return;
            }

            int afterColon = key.Length + 1;
            int valueStart = FindValueStart(line.Content, afterColon);
            int commentStart = FindInlineCommentStart(line.Content, afterColon);
            if (valueStart > commentStart)
            {
                valueStart = commentStart;
            }

            int valueEnd = commentStart;
            while (valueEnd > valueStart && char.IsWhiteSpace(line.Content[valueEnd - 1]))
            {
                valueEnd--;
            }

            line.Content = line.Content.Substring(0, valueStart)
                + value
                + line.Content.Substring(valueEnd);
        }

        public bool Save()
        {
            string currentText = BuildText();
            if (string.Equals(currentText, _originalText, StringComparison.Ordinal))
            {
                return false;
            }

            string directory = Path.GetDirectoryName(_path);
            string fileName = Path.GetFileName(_path);
            string temporaryPath = Path.Combine(
                directory,
                "." + fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                WriteUtf8(temporaryPath, currentText);
                File.Replace(temporaryPath, _path, null, true);
                _originalText = currentText;
                return true;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private int FindKeyLine(string key)
        {
            string prefix = key + ":";
            for (int i = 1; i < _closingDelimiterIndex; i++)
            {
                if (_lines[i].Content.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string ParseValue(string line, string key)
        {
            int afterColon = key.Length + 1;
            int commentStart = FindInlineCommentStart(line, afterColon);
            int valueStart = FindValueStart(line, afterColon);
            if (valueStart > commentStart)
            {
                valueStart = commentStart;
            }

            int valueEnd = commentStart;
            while (valueEnd > valueStart && char.IsWhiteSpace(line[valueEnd - 1]))
            {
                valueEnd--;
            }

            string value = line.Substring(valueStart, valueEnd - valueStart);
            if (value.Length >= 2
                && ((value[0] == '"' && value[value.Length - 1] == '"')
                    || (value[0] == '\'' && value[value.Length - 1] == '\'')))
            {
                value = value.Substring(1, value.Length - 2);
            }

            return value;
        }

        private static int FindValueStart(string line, int afterColon)
        {
            int valueStart = afterColon;
            while (valueStart < line.Length && char.IsWhiteSpace(line[valueStart]))
            {
                valueStart++;
            }

            return valueStart;
        }

        private static int FindInlineCommentStart(string line, int afterColon)
        {
            for (int i = afterColon; i < line.Length; i++)
            {
                if (line[i] != '#' || i == afterColon || !char.IsWhiteSpace(line[i - 1]))
                {
                    continue;
                }

                int commentStart = i - 1;
                while (commentStart > afterColon && char.IsWhiteSpace(line[commentStart - 1]))
                {
                    commentStart--;
                }

                return commentStart;
            }

            return line.Length;
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("キーは空にできない", nameof(key));
            }

            if (key.IndexOf('\r') >= 0 || key.IndexOf('\n') >= 0)
            {
                throw new ArgumentException("キーに改行を含めることはできない", nameof(key));
            }
        }

        private static int FindClosingDelimiter(List<TextLine> lines)
        {
            for (int i = 1; i < lines.Count; i++)
            {
                if (lines[i].Content == Delimiter)
                {
                    return i;
                }
            }

            return -1;
        }

        private static List<TextLine> SplitLines(string text)
        {
            var lines = new List<TextLine>();
            int start = 0;

            while (start < text.Length)
            {
                int lineStart = start;
                int end = start;
                while (end < text.Length && text[end] != '\r' && text[end] != '\n')
                {
                    end++;
                }

                if (end == text.Length)
                {
                    lines.Add(new TextLine(text.Substring(start), string.Empty));
                    start = text.Length;
                    break;
                }

                string terminator;
                if (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n')
                {
                    terminator = "\r\n";
                    start = end + 2;
                }
                else
                {
                    terminator = text[end].ToString();
                    start = end + 1;
                }

                lines.Add(new TextLine(text.Substring(lineStart, end - lineStart), terminator));
            }

            if (lines.Count == 0 || lines[lines.Count - 1].Terminator.Length > 0)
            {
                lines.Add(new TextLine(string.Empty, string.Empty));
            }

            return lines;
        }

        private static string FindFirstNewline(List<TextLine> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Terminator.Length > 0)
                {
                    return lines[i].Terminator;
                }
            }

            return null;
        }

        private string BuildText()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < _lines.Count; i++)
            {
                builder.Append(_lines[i].Content);
                builder.Append(_lines[i].Terminator);
            }

            return builder.ToString();
        }

        private static bool HasUtf8Bom(byte[] bytes)
        {
            return bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF;
        }

        private static string DecodeUtf8(byte[] bytes, bool hasBom)
        {
            var encoding = new UTF8Encoding(false, true);
            int offset = hasBom ? 3 : 0;
            return encoding.GetString(bytes, offset, bytes.Length - offset);
        }

        private void WriteUtf8(string path, string text)
        {
            var encoding = new UTF8Encoding(false, true);
            byte[] content = encoding.GetBytes(text);
            byte[] preamble = _hasUtf8Bom ? Utf8Bom : new byte[0];

            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (preamble.Length > 0)
                {
                    stream.Write(preamble, 0, preamble.Length);
                }

                stream.Write(content, 0, content.Length);
                stream.Flush(true);
            }
        }

        private sealed class TextLine
        {
            public TextLine(string content, string terminator)
            {
                Content = content;
                Terminator = terminator;
            }

            public string Content { get; set; }

            public string Terminator { get; private set; }
        }
    }
}
