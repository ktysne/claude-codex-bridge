using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace CodexBridgeConsole
{
    public sealed class ConsoleSettings
    {
        private const string CodexEnabledKey = "codex_enabled";

        private const string ScalarRuleText = " (使えるのは英数字と . _ - / だけである)";
        private const string CodexHomeKey = "codex_home";
        private const string CodexSandboxKey = "codex_sandbox";

        private static readonly string[] ValidGptEfforts =
        {
            "low",
            "medium",
            "high",
            "xhigh",
            "max"
        };

        private static readonly DefinitionPath[] DefinitionPaths =
        {
            new DefinitionPath(Path.Combine("agents", "impl-hard.md"), DefinitionKind.ClaudeHard),
            new DefinitionPath(Path.Combine("agents", "impl-standard.md"), DefinitionKind.ClaudeStandard),
            new DefinitionPath(Path.Combine("agents", "impl-light.md"), DefinitionKind.ClaudeLight),
            new DefinitionPath(Path.Combine("gpt-agents", "impl-standard.md"), DefinitionKind.GptStandard),
            new DefinitionPath(Path.Combine("gpt-agents", "impl-light.md"), DefinitionKind.GptLight)
        };

        private readonly Dictionary<string, FrontMatterFile> _files =
            new Dictionary<string, FrontMatterFile>(StringComparer.OrdinalIgnoreCase);
        private AgentSettings _loadedImplHard;
        private AgentSettings _loadedImplStandard;
        private AgentSettings _loadedImplLight;
        private bool _loadedCodexEnabled;

        public ConsoleSettings()
            : this(null)
        {
        }

        public ConsoleSettings(string rootDirectory)
        {
            RootDirectory = Path.GetFullPath(
                string.IsNullOrEmpty(rootDirectory) ? GetDefaultRootDirectory() : rootDirectory);
            Reload();
        }

        public string RootDirectory { get; private set; }

        public AgentSettings ImplHard { get; private set; }

        public AgentSettings ImplStandard { get; private set; }

        public AgentSettings ImplLight { get; private set; }

        public bool CodexEnabled { get; set; }

        public bool CodexEnabledMismatch { get; private set; }

        // 画面でトグルを操作したことを示す。2 定義の値が食い違っているとき、
        // 表示上の値が変わらなくても両方へ書き戻せるようにするためである。
        public bool CodexEnabledExplicit { get; set; }

        public string CodexHome { get; private set; }

        public string ExpandedCodexHome { get; private set; }

        public bool CodexHomeExists { get; private set; }

        public string CodexSandbox { get; private set; }

        public IReadOnlyList<string> MissingFiles { get; private set; }

        public IReadOnlyList<string> LastChangedFiles { get; private set; }

        public IReadOnlyList<string> LastValidationErrors { get; private set; }

        public IReadOnlyList<string> UnreadableFiles { get; private set; }

        public bool CanSave
        {
            get { return MissingFiles.Count == 0 && UnreadableFiles.Count == 0; }
        }

        public bool HasChanges
        {
            get
            {
                // トグルを操作すると、表示上の値が読み込み時と同じでも保存結果が変わる。
                // 未保存の変更として扱わないと、確認なしに操作が失われる。
                return CodexEnabledExplicit
                    || CodexEnabled != _loadedCodexEnabled
                    || !ImplHard.HasSameValues(_loadedImplHard)
                    || !ImplStandard.HasSameValues(_loadedImplStandard)
                    || !ImplLight.HasSameValues(_loadedImplLight);
            }
        }

        public void Reload()
        {
            var missingFiles = new List<string>();
            var unreadableFiles = new List<string>();

            // すべて読み終えてから差し替える。
            // 途中で失敗したときに、ファイル一覧と画面の値が食い違った状態を残さないためである。
            var loadedFiles = new Dictionary<string, FrontMatterFile>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < DefinitionPaths.Length; i++)
            {
                DefinitionPath definition = DefinitionPaths[i];
                string path = Path.Combine(RootDirectory, definition.RelativePath);
                if (!File.Exists(path))
                {
                    missingFiles.Add(definition.RelativePath);
                    continue;
                }

                try
                {
                    loadedFiles.Add(definition.RelativePath, FrontMatterFile.Load(path));
                }
                catch (Exception exception) when (
                    exception is InvalidDataException
                    || exception is IOException
                    || exception is UnauthorizedAccessException)
                {
                    // 読めない定義があっても画面は開く。定義を直すための道具が、
                    // 定義が壊れているときに起動できないと使えないためである。
                    unreadableFiles.Add(definition.RelativePath + ": " + exception.Message);
                }
            }

            _files.Clear();
            foreach (KeyValuePair<string, FrontMatterFile> entry in loadedFiles)
            {
                _files.Add(entry.Key, entry.Value);
            }

            ImplHard = ReadClaudeSettings(DefinitionKind.ClaudeHard);
            ImplStandard = ReadClaudeSettings(DefinitionKind.ClaudeStandard);
            ImplLight = ReadClaudeSettings(DefinitionKind.ClaudeLight);

            FrontMatterFile gptLight;
            FrontMatterFile gptStandard;
            bool hasLight = _files.TryGetValue(GetRelativePath(DefinitionKind.GptLight), out gptLight);
            bool hasStandard = _files.TryGetValue(GetRelativePath(DefinitionKind.GptStandard), out gptStandard);

            // トグルは 1 つで 2 定義を切り替えるため、両方が有効なときだけ有効として表示する。
            // 片方だけ無効の状態を有効と読むと、別の項目を保存したときに無効側が有効へ戻る。
            bool lightEnabled = !hasLight || ReadEffectiveCodexEnabled(gptLight);
            bool standardEnabled = !hasStandard || ReadEffectiveCodexEnabled(gptStandard);
            CodexEnabled = lightEnabled && standardEnabled;
            CodexEnabledMismatch = hasLight && hasStandard && lightEnabled != standardEnabled;

            if (hasLight)
            {
                CodexHome = gptLight.GetValue(CodexHomeKey);
                CodexSandbox = gptLight.GetValue(CodexSandboxKey);
            }
            else
            {
                CodexHome = null;
                CodexSandbox = null;
            }

            ExpandedCodexHome = ExpandCodexHome(CodexHome);
            CodexHomeExists = !string.IsNullOrEmpty(ExpandedCodexHome)
                && Directory.Exists(ExpandedCodexHome);

            ReadGptSettings(ImplStandard, DefinitionKind.GptStandard);
            ReadGptSettings(ImplLight, DefinitionKind.GptLight);

            MissingFiles = ReadOnly(missingFiles);
            UnreadableFiles = ReadOnly(unreadableFiles);
            LastChangedFiles = ReadOnly(new List<string>());
            LastValidationErrors = ReadOnly(new List<string>());
            SaveLoadedValues();
        }

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            for (int i = 0; i < MissingFiles.Count; i++)
            {
                errors.Add("定義ファイルが存在しない: " + MissingFiles[i]);
            }

            ValidateClaude(ImplHard, GetRelativePath(DefinitionKind.ClaudeHard), errors);
            ValidateClaude(ImplStandard, GetRelativePath(DefinitionKind.ClaudeStandard), errors);
            ValidateClaude(ImplLight, GetRelativePath(DefinitionKind.ClaudeLight), errors);

            ValidateGpt(
                ImplStandard,
                GetRelativePath(DefinitionKind.GptStandard),
                errors);
            ValidateGpt(
                ImplLight,
                GetRelativePath(DefinitionKind.GptLight),
                errors);

            return ReadOnly(errors);
        }

        public ConsoleSettingsSaveResult Save()
        {
            IReadOnlyList<string> validationErrors = Validate();
            if (validationErrors.Count > 0)
            {
                LastValidationErrors = validationErrors;
                LastChangedFiles = ReadOnly(new List<string>());
                return new ConsoleSettingsSaveResult(
                    false,
                    validationErrors,
                    LastChangedFiles);
            }

            ApplyClaude(ImplHard, DefinitionKind.ClaudeHard);
            ApplyClaude(ImplStandard, DefinitionKind.ClaudeStandard);
            ApplyClaude(ImplLight, DefinitionKind.ClaudeLight);
            ApplyGpt(ImplStandard, DefinitionKind.GptStandard);
            ApplyGpt(ImplLight, DefinitionKind.GptLight);

            var changedFiles = new List<string>();
            LastChangedFiles = ReadOnly(changedFiles);
            try
            {
                for (int i = 0; i < DefinitionPaths.Length; i++)
                {
                    DefinitionPath definition = DefinitionPaths[i];
                    FrontMatterFile file;
                    if (_files.TryGetValue(definition.RelativePath, out file) && file.Save())
                    {
                        changedFiles.Add(definition.RelativePath);
                    }
                }
            }
            finally
            {
                // 書き込みは 1 ファイルずつ行う。どの経路で中断しても、
                // 途中まで保存できたファイルを呼び出し側へ伝えられるようにする。
                LastChangedFiles = ReadOnly(changedFiles);
            }

            RefreshCodexEnabledMismatch();
            LastValidationErrors = ReadOnly(new List<string>());
            SaveLoadedValues();
            return new ConsoleSettingsSaveResult(
                true,
                LastValidationErrors,
                LastChangedFiles);
        }

        private AgentSettings ReadClaudeSettings(DefinitionKind kind)
        {
            FrontMatterFile file;
            if (!_files.TryGetValue(GetRelativePath(kind), out file))
            {
                return new AgentSettings();
            }

            return new AgentSettings
            {
                ClaudeModel = file.GetValue("model"),
                ClaudeEffort = file.GetValue("effort")
            };
        }

        private void ReadGptSettings(AgentSettings settings, DefinitionKind kind)
        {
            FrontMatterFile file;
            if (!_files.TryGetValue(GetRelativePath(kind), out file))
            {
                return;
            }

            settings.CodexModel = file.GetValue("codex_model");
            settings.CodexReasoningEffort = file.GetValue("codex_reasoning_effort");
        }

        private void ValidateClaude(
            AgentSettings settings,
            string relativePath,
            List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(settings.ClaudeModel))
            {
                errors.Add(relativePath + " の model が空である");
            }
            else if (!IsSafeScalar(settings.ClaudeModel))
            {
                errors.Add(relativePath + " の model に使えない文字がある: " + settings.ClaudeModel + ScalarRuleText);
            }

            if (string.IsNullOrWhiteSpace(settings.ClaudeEffort))
            {
                errors.Add(relativePath + " の effort が空である");
            }
            else if (!IsSafeScalar(settings.ClaudeEffort))
            {
                errors.Add(relativePath + " の effort に使えない文字がある: " + settings.ClaudeEffort + ScalarRuleText);
            }
        }

        // フロントマターへ引用符なしで書くため、YAML の意味を変える文字を通さない。
        // Claude 側の定義は Claude Code が YAML として読むため、model: foo: bar のような値で壊れる。
        private static bool IsSafeScalar(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool allowed = (c >= 'A' && c <= 'Z')
                    || (c >= 'a' && c <= 'z')
                    || (c >= '0' && c <= '9')
                    || c == '.'
                    || c == '_'
                    || c == '-'
                    || c == '/';
                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }

        private void ValidateGpt(
            AgentSettings settings,
            string relativePath,
            List<string> errors)
        {
            if (CodexEnabled && string.IsNullOrWhiteSpace(settings.CodexModel))
            {
                errors.Add(relativePath + " の codex_model が空である");
            }
            else if (!string.IsNullOrWhiteSpace(settings.CodexModel) && !IsSafeScalar(settings.CodexModel))
            {
                errors.Add(relativePath + " の codex_model に使えない文字がある: " + settings.CodexModel + ScalarRuleText);
            }

            if (!IsValidGptEffort(settings.CodexReasoningEffort))
            {
                errors.Add(
                    relativePath
                    + " の codex_reasoning_effort が不正である: "
                    + (settings.CodexReasoningEffort ?? "(未設定)")
                    + " (low、medium、high、xhigh、max のいずれか)");
            }
        }

        private void ApplyClaude(AgentSettings settings, DefinitionKind kind)
        {
            FrontMatterFile file = GetFile(kind);
            file.SetValue("model", settings.ClaudeModel);
            file.SetValue("effort", settings.ClaudeEffort);
        }

        private void ApplyGpt(AgentSettings settings, DefinitionKind kind)
        {
            FrontMatterFile file = GetFile(kind);

            // トグルを操作していないときは codex_enabled に触れない。
            // 2 定義の値が食い違っている場合に、片方を黙って書き換えないためである。
            if (CodexEnabled != _loadedCodexEnabled || CodexEnabledExplicit)
            {
                SetCodexEnabled(file, CodexEnabled);
            }
            file.SetValue("codex_model", settings.CodexModel);
            file.SetValue("codex_reasoning_effort", settings.CodexReasoningEffort);
        }

        private static void SetCodexEnabled(FrontMatterFile file, bool enabled)
        {
            string currentValue;
            string targetValue = enabled ? "true" : "false";
            if (!file.TryGetValue(CodexEnabledKey, out currentValue))
            {
                if (!enabled)
                {
                    file.SetValue(CodexEnabledKey, targetValue);
                }

                return;
            }

            if (!string.Equals(currentValue, targetValue, StringComparison.Ordinal))
            {
                file.SetValue(CodexEnabledKey, targetValue);
            }
        }

        // 保存で 2 定義の codex_enabled を揃えた場合に、食い違いの表示を残さないため読み直す。
        private void RefreshCodexEnabledMismatch()
        {
            FrontMatterFile gptLight;
            FrontMatterFile gptStandard;
            bool hasLight = _files.TryGetValue(GetRelativePath(DefinitionKind.GptLight), out gptLight);
            bool hasStandard = _files.TryGetValue(GetRelativePath(DefinitionKind.GptStandard), out gptStandard);
            CodexEnabledMismatch = hasLight
                && hasStandard
                && ReadEffectiveCodexEnabled(gptLight) != ReadEffectiveCodexEnabled(gptStandard);
        }

        private FrontMatterFile GetFile(DefinitionKind kind)
        {
            return _files[GetRelativePath(kind)];
        }

        private static bool ReadEffectiveCodexEnabled(FrontMatterFile file)
        {
            string value = file.GetValue(CodexEnabledKey);
            return !string.Equals(value, "false", StringComparison.Ordinal);
        }

        private static bool IsValidGptEffort(string value)
        {
            if (value == null)
            {
                return false;
            }

            for (int i = 0; i < ValidGptEfforts.Length; i++)
            {
                if (string.Equals(value, ValidGptEfforts[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void SaveLoadedValues()
        {
            _loadedImplHard = ImplHard.Clone();
            _loadedImplStandard = ImplStandard.Clone();
            _loadedImplLight = ImplLight.Clone();
            _loadedCodexEnabled = CodexEnabled;
            CodexEnabledExplicit = false;
        }

        private static string GetDefaultRootDirectory()
        {
            return Path.Combine(GetUserProfile(), ".claude");
        }

        private static string GetUserProfile()
        {
            string userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (string.IsNullOrEmpty(userProfile))
            {
                userProfile = Environment.GetEnvironmentVariable("HOME");
            }

            if (string.IsNullOrEmpty(userProfile))
            {
                userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            return userProfile;
        }

        private static string ExpandCodexHome(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            string userProfile = GetUserProfile();
            string expanded = value;
            if (string.Equals(expanded, "~", StringComparison.Ordinal))
            {
                expanded = userProfile;
            }
            else if (expanded.StartsWith("~/", StringComparison.Ordinal)
                || expanded.StartsWith("~\\", StringComparison.Ordinal))
            {
                expanded = Path.Combine(userProfile, expanded.Substring(2));
            }

            expanded = expanded.Replace("$USERPROFILE", userProfile);
            expanded = expanded.Replace("%USERPROFILE%", userProfile);

            try
            {
                return Path.GetFullPath(expanded);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private static string GetRelativePath(DefinitionKind kind)
        {
            for (int i = 0; i < DefinitionPaths.Length; i++)
            {
                if (DefinitionPaths[i].Kind == kind)
                {
                    return DefinitionPaths[i].RelativePath;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        private static IReadOnlyList<string> ReadOnly(List<string> values)
        {
            // 元のリストをそのまま包むと、後から要素を足したときに公開済みの一覧まで変わる。
            // 代入した時点の内容を保つため写しを返す。
            return new ReadOnlyCollection<string>(new List<string>(values));
        }

        private enum DefinitionKind
        {
            ClaudeHard,
            ClaudeStandard,
            ClaudeLight,
            GptStandard,
            GptLight
        }

        private sealed class DefinitionPath
        {
            public DefinitionPath(string relativePath, DefinitionKind kind)
            {
                RelativePath = relativePath;
                Kind = kind;
            }

            public string RelativePath { get; private set; }

            public DefinitionKind Kind { get; private set; }
        }
    }

    public sealed class AgentSettings
    {
        public string ClaudeModel { get; set; }

        public string ClaudeEffort { get; set; }

        public string CodexModel { get; set; }

        public string CodexReasoningEffort { get; set; }

        internal AgentSettings Clone()
        {
            return new AgentSettings
            {
                ClaudeModel = ClaudeModel,
                ClaudeEffort = ClaudeEffort,
                CodexModel = CodexModel,
                CodexReasoningEffort = CodexReasoningEffort
            };
        }

        internal bool HasSameValues(AgentSettings other)
        {
            return other != null
                && string.Equals(ClaudeModel, other.ClaudeModel, StringComparison.Ordinal)
                && string.Equals(ClaudeEffort, other.ClaudeEffort, StringComparison.Ordinal)
                && string.Equals(CodexModel, other.CodexModel, StringComparison.Ordinal)
                && string.Equals(CodexReasoningEffort, other.CodexReasoningEffort, StringComparison.Ordinal);
        }
    }

    public sealed class ConsoleSettingsSaveResult
    {
        internal ConsoleSettingsSaveResult(
            bool succeeded,
            IReadOnlyList<string> validationErrors,
            IReadOnlyList<string> changedFiles)
        {
            Succeeded = succeeded;
            ValidationErrors = validationErrors;
            ChangedFiles = changedFiles;
        }

        public bool Succeeded { get; private set; }

        public IReadOnlyList<string> ValidationErrors { get; private set; }

        public IReadOnlyList<string> ChangedFiles { get; private set; }
    }
}
