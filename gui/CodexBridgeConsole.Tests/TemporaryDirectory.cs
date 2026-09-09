using System;
using System.IO;
using System.Text;

namespace CodexBridgeConsole.Tests
{
    internal sealed class TemporaryDirectory : IDisposable
    {
        internal static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexBridgeConsoleTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; private set; }

        public string WriteFile(string fileName, string text)
        {
            return WriteFile(fileName, text, false);
        }

        public string WriteFile(string fileName, string text, bool bom)
        {
            string filePath = System.IO.Path.Combine(Path, fileName);
            File.WriteAllBytes(filePath, EncodeUtf8(text, bom));
            return filePath;
        }

        public string ReadText(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            bool hasBom = bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF;
            int offset = hasBom ? 3 : 0;
            return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        }

        internal static byte[] EncodeUtf8(string text, bool bom)
        {
            var encoding = new UTF8Encoding(false, true);
            byte[] content = encoding.GetBytes(text);
            if (!bom)
            {
                return content;
            }

            // GetPreamble() は encoderShouldEmitUTF8Identifier が false のとき空を返すため、BOM は直接書く。
            byte[] result = new byte[Utf8Bom.Length + content.Length];
            Buffer.BlockCopy(Utf8Bom, 0, result, 0, Utf8Bom.Length);
            Buffer.BlockCopy(content, 0, result, Utf8Bom.Length, content.Length);
            return result;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
