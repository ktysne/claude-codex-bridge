using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace CodexBridgeConsole
{
    // codex debug models の出力から、GPT 側のモデルと effort の選択肢を取り出す。
    // 取得できないときは null を返し、呼び出し側は Choices の既定値へ戻す。
    public sealed class CodexModelCatalog
    {
        private const int LoadTimeoutMilliseconds = 15000;

        private const int KillTimeoutMilliseconds = 2000;

        private const string ListedVisibility = "list";

        private static readonly IReadOnlyList<string> EmptyEfforts =
            new ReadOnlyCollection<string>(new List<string>());

        private readonly IReadOnlyList<string> _models;
        private readonly Dictionary<string, IReadOnlyList<string>> _effortsByModel;
        private readonly Dictionary<string, string> _defaultEffortByModel;

        private CodexModelCatalog(
            IReadOnlyList<string> models,
            Dictionary<string, IReadOnlyList<string>> effortsByModel,
            Dictionary<string, string> defaultEffortByModel)
        {
            _models = models;
            _effortsByModel = effortsByModel;
            _defaultEffortByModel = defaultEffortByModel;
        }

        // visibility が list のモデルの slug を、JSON に現れた順で返す。
        public IReadOnlyList<string> Models
        {
            get { return _models; }
        }

        // 知らないモデルには空を返す。呼び出し側で選択肢を消さずに済ませるためである。
        public IReadOnlyList<string> EffortsFor(string model)
        {
            IReadOnlyList<string> efforts;
            if (model != null && _effortsByModel.TryGetValue(model, out efforts))
            {
                return efforts;
            }

            return EmptyEfforts;
        }

        public string DefaultEffortFor(string model)
        {
            string effort;
            if (model != null && _defaultEffortByModel.TryGetValue(model, out effort))
            {
                return effort;
            }

            return null;
        }

        // 取得できない場合はすべて null を返す。画面は既定の選択肢のまま動く。
        public static CodexModelCatalog Load(string codexHome)
        {
            // 認証ホームが分からないまま codex を起動しない。
            // 既定の ~/.codex や親プロセスの環境変数へ暗黙に依存する呼び出しを作らないためである。
            if (string.IsNullOrEmpty(codexHome))
            {
                return null;
            }

            try
            {
                // Windows の npm は codex.cmd を置く。UseShellExecute = false で "codex" を直接起動すると
                // .cmd を解決できず、導入済みでも見つからない扱いになる。PATH の解決を cmd に任せる。
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c codex debug models",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                // 認証ホームは常に明示する。既定の ~/.codex に暗黙に依存する呼び出しを作らない。
                startInfo.EnvironmentVariables["CODEX_HOME"] = codexHome;

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();

                    // 出力は数百 KB になる。終了を待ってから読むとパイプが詰まって止まるため、
                    // 走らせながら読み切る。
                    Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                    Task<string> standardError = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(LoadTimeoutMilliseconds))
                    {
                        // 起動したのは cmd であり、codex 本体はその子である。
                        // cmd だけを止めても子が残るため、プロセスツリーごと落とす。
                        KillProcessTree(process);
                        return null;
                    }

                    standardError.Wait(KillTimeoutMilliseconds);
                    if (!standardOutput.Wait(KillTimeoutMilliseconds))
                    {
                        return null;
                    }

                    if (process.ExitCode != 0)
                    {
                        return null;
                    }

                    // 出力にはモデルの内部指示文が含まれる。ファイルやログへ残さず、
                    // 必要な項目だけを取り出してその場で捨てる。
                    return Parse(standardOutput.Result);
                }
            }
            catch (Exception exception) when (
                exception is Win32Exception
                || exception is InvalidOperationException
                || exception is IOException
                || exception is AggregateException)
            {
                return null;
            }
        }

        // 解析できない場合は null を返す。呼び出し側は既定の選択肢へ戻す。
        public static CodexModelCatalog Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                ModelListDocument document;

                // net48 の標準ライブラリで JSON を扱えるため、Choices と同じく DataContractJsonSerializer を使う。
                var serializer = new DataContractJsonSerializer(typeof(ModelListDocument));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    document = serializer.ReadObject(stream) as ModelListDocument;
                }

                if (document == null || document.Models == null)
                {
                    return null;
                }

                var models = new List<string>();
                var effortsByModel = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                var defaultEffortByModel = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < document.Models.Count; i++)
                {
                    ModelEntry entry = document.Models[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Slug))
                    {
                        continue;
                    }

                    if (!string.Equals(entry.Visibility, ListedVisibility, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (entry.SupportedReasoningLevels == null)
                    {
                        continue;
                    }

                    var efforts = new List<string>();
                    for (int j = 0; j < entry.SupportedReasoningLevels.Count; j++)
                    {
                        ReasoningLevelEntry level = entry.SupportedReasoningLevels[j];
                        if (level == null || string.IsNullOrWhiteSpace(level.Effort))
                        {
                            continue;
                        }

                        efforts.Add(level.Effort);
                    }

                    if (effortsByModel.ContainsKey(entry.Slug))
                    {
                        continue;
                    }

                    models.Add(entry.Slug);
                    effortsByModel.Add(entry.Slug, new ReadOnlyCollection<string>(efforts));
                    defaultEffortByModel.Add(entry.Slug, entry.DefaultReasoningLevel);
                }

                if (models.Count == 0)
                {
                    return null;
                }

                return new CodexModelCatalog(
                    new ReadOnlyCollection<string>(models),
                    effortsByModel,
                    defaultEffortByModel);
            }
            catch (Exception exception) when (
                exception is SerializationException
                || exception is XmlException
                || exception is ArgumentException
                || exception is InvalidDataException)
            {
                return null;
            }
        }

        private static void KillProcessTree(Process process)
        {
            try
            {
                using (var killer = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = "/PID " + process.Id + " /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }))
                {
                    if (killer != null)
                    {
                        killer.WaitForExit(KillTimeoutMilliseconds);
                    }
                }
            }
            catch (Exception exception) when (
                exception is Win32Exception || exception is InvalidOperationException)
            {
                // taskkill を起動できない場合に備え、少なくとも自分が起動した cmd は止める。
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception exception) when (
                exception is Win32Exception || exception is InvalidOperationException)
            {
                // 既に終了している場合は何もしない。画面の起動を待たせないためである。
            }
        }

        [DataContract]
        private sealed class ModelListDocument
        {
            [DataMember(Name = "models")]
            public List<ModelEntry> Models { get; set; }
        }

        [DataContract]
        private sealed class ModelEntry
        {
            [DataMember(Name = "slug")]
            public string Slug { get; set; }

            [DataMember(Name = "visibility")]
            public string Visibility { get; set; }

            [DataMember(Name = "default_reasoning_level")]
            public string DefaultReasoningLevel { get; set; }

            [DataMember(Name = "supported_reasoning_levels")]
            public List<ReasoningLevelEntry> SupportedReasoningLevels { get; set; }
        }

        [DataContract]
        private sealed class ReasoningLevelEntry
        {
            [DataMember(Name = "effort")]
            public string Effort { get; set; }
        }
    }
}
