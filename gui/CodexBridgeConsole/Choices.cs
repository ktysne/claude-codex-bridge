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

        private Choices(ChoiceDocument document)
        {
            ClaudeModels = Copy(document.ClaudeModels);
            ClaudeEfforts = Copy(document.ClaudeEfforts);
            GptModels = Copy(document.GptModels);
            GptEfforts = Copy(document.GptEfforts);
        }

        public IReadOnlyList<string> ClaudeModels { get; private set; }

        public IReadOnlyList<string> ClaudeEfforts { get; private set; }

        public IReadOnlyList<string> GptModels { get; private set; }

        public IReadOnlyList<string> GptEfforts { get; private set; }

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

            [DataMember(Name = "gptModels")]
            public List<string> GptModels { get; set; }

            [DataMember(Name = "gptEfforts")]
            public List<string> GptEfforts { get; set; }
        }
    }
}
