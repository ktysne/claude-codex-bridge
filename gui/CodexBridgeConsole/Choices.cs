using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Xml;

namespace CodexBridgeConsole
{
    public sealed class Choices
    {
        private const string DefaultResourceName = "CodexBridgeConsole.choices.default.json";

        // Claude Code は、モデルが受け付けない effort を指定されると、指定値以下で最も高い対応済みの値へ落として実行する。
        // その規則に合わせて選択肢を寄せるため、段階の並びをここに固定する。
        private static readonly string[] EffortOrder = { "low", "medium", "high", "xhigh", "max", "ultra" };

        private readonly Dictionary<string, IReadOnlyList<string>> _claudeModelEfforts;

        private Choices(ChoiceDocument document)
        {
            ClaudeModels = Copy(document.ClaudeModels);
            ClaudeEfforts = Copy(document.ClaudeEfforts);
            GptModels = Copy(document.GptModels);
            GptEfforts = Copy(document.GptEfforts);
            _claudeModelEfforts = BuildClaudeModelEfforts(document.ClaudeModelEfforts);
        }

        public IReadOnlyList<string> ClaudeModels { get; private set; }

        public IReadOnlyList<string> ClaudeEfforts { get; private set; }

        public IReadOnlyList<string> GptModels { get; private set; }

        public IReadOnlyList<string> GptEfforts { get; private set; }

        // Claude Code には非対話でモデル一覧を返すコマンドが無いため、モデルごとの effort は設定として持つ。
        // 対応表に無いモデルには平坦な一覧を返す。選択肢を消さずに済ませるためである。
        public IReadOnlyList<string> ClaudeEffortsFor(string model)
        {
            IReadOnlyList<string> efforts;
            if (!string.IsNullOrEmpty(model) && _claudeModelEfforts.TryGetValue(model, out efforts))
            {
                return efforts;
            }

            return ClaudeEfforts;
        }

        // Claude Code が「指定値以下で最も高い対応済みの effort へ落とす」と定めているため、その規則に合わせる。
        public static string NearestSupportedEffort(IReadOnlyList<string> efforts, string current)
        {
            if (efforts == null || efforts.Count == 0)
            {
                return current;
            }

            if (Contains(efforts, current))
            {
                return current;
            }

            int currentIndex = IndexOfOrder(current);
            if (currentIndex < 0)
            {
                // 段階の並びに無い値は利用者が手で入れた綴りである。勝手に置き換えない。
                return current;
            }

            for (int i = currentIndex - 1; i >= 0; i--)
            {
                if (Contains(efforts, EffortOrder[i]))
                {
                    return EffortOrder[i];
                }
            }

            return efforts[0];
        }

        public static Choices Load()
        {
            return Load(AppDomain.CurrentDomain.BaseDirectory);
        }

        public static Choices Load(string directory)
        {
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            Choices defaults = LoadEmbeddedDefaults();
            string choicesPath = Path.Combine(directory, "choices.json");
            if (!File.Exists(choicesPath))
            {
                return defaults;
            }

            try
            {
                using (var stream = File.OpenRead(choicesPath))
                {
                    return ReadChoices(stream);
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is IOException
                || exception is InvalidDataException
                || exception is SerializationException
                || exception is UnauthorizedAccessException
                || exception is XmlException)
            {
                // choices.json が壊れている、またはキーが欠けている場合は埋め込み既定値に戻す。任意ファイルの不備で設定画面を起動できなくするより、既定値で継続できる方が安全だからである。
                return defaults;
            }
        }

        private static Choices LoadEmbeddedDefaults()
        {
            Assembly assembly = typeof(Choices).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(DefaultResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("埋め込みの既定選択肢が見つからない");
                }

                return ReadChoices(stream);
            }
        }

        private static Choices ReadChoices(Stream stream)
        {
            // net48 の標準ライブラリで JSON を扱え、選択肢の配列だけを検証できるため、DataContractJsonSerializer を使う。
            var serializer = new DataContractJsonSerializer(typeof(ChoiceDocument));
            var document = serializer.ReadObject(stream) as ChoiceDocument;
            Validate(document);
            return new Choices(document);
        }

        private static void Validate(ChoiceDocument document)
        {
            if (document == null)
            {
                throw new InvalidDataException("選択肢 JSON がオブジェクトではない");
            }

            ValidateList(document.ClaudeModels, "claudeModels");
            ValidateList(document.ClaudeEfforts, "claudeEfforts");
            ValidateList(document.GptModels, "gptModels");
            ValidateList(document.GptEfforts, "gptEfforts");
        }

        private static void ValidateList(List<string> values, string name)
        {
            if (values == null || values.Count == 0)
            {
                throw new InvalidDataException(name + " が無い、または空である");
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(values[i]))
                {
                    throw new InvalidDataException(name + " に空の選択肢がある");
                }
            }
        }

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int IndexOfOrder(string effort)
        {
            for (int i = 0; i < EffortOrder.Length; i++)
            {
                if (string.Equals(EffortOrder[i], effort, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        // claudeModelEfforts は任意の項目である。無い場合や項目が壊れている場合は、その項目だけを捨てて空の対応表にする。
        // 既存の choices.json をそのまま使えるようにするためである。
        private static Dictionary<string, IReadOnlyList<string>> BuildClaudeModelEfforts(
            List<ModelEffortEntry> entries)
        {
            var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            if (entries == null)
            {
                return map;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                ModelEffortEntry entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Model) || entry.Efforts == null)
                {
                    continue;
                }

                var efforts = new List<string>();
                for (int j = 0; j < entry.Efforts.Count; j++)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Efforts[j]))
                    {
                        efforts.Add(entry.Efforts[j]);
                    }
                }

                if (efforts.Count == 0 || map.ContainsKey(entry.Model))
                {
                    continue;
                }

                map.Add(entry.Model, new ReadOnlyCollection<string>(efforts));
            }

            return map;
        }

        private static IReadOnlyList<string> Copy(List<string> values)
        {
            return new ReadOnlyCollection<string>(new List<string>(values));
        }

        [DataContract]
        private sealed class ChoiceDocument
        {
            [DataMember(Name = "claudeModels")]
            public List<string> ClaudeModels { get; set; }

            [DataMember(Name = "claudeEfforts")]
            public List<string> ClaudeEfforts { get; set; }

            [DataMember(Name = "claudeModelEfforts")]
            public List<ModelEffortEntry> ClaudeModelEfforts { get; set; }

            [DataMember(Name = "gptModels")]
            public List<string> GptModels { get; set; }

            [DataMember(Name = "gptEfforts")]
            public List<string> GptEfforts { get; set; }
        }

        [DataContract]
        private sealed class ModelEffortEntry
        {
            [DataMember(Name = "model")]
            public string Model { get; set; }

            [DataMember(Name = "efforts")]
            public List<string> Efforts { get; set; }
        }
    }
}
