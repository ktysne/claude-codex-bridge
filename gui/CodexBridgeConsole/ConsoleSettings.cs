using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace CodexBridgeConsole
{
    public sealed class ConsoleSettings
    {
        private const string CodexEnabledKey = "codex_enabled";

        private const string ScalarRuleText = " (使えるのは英数字と . _ - / だけである)";

        private const string DefaultGptEffort = "medium";
        private const string CodexHomeKey = "codex_home";
        private const string CodexSandboxKey = "codex_sandbox";

        private static readonly string[] ValidGptEfforts =
        {
            "low",
            "medium",
            "high",
            "xhigh",
            "max",
            "ultra"
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
        private bool _saveInterrupted;

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

        public IReadOnlyList<string> CodexEnabledInvalidFiles { get; private set; }

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

        // codex_enabled の不正値は、利用者が何も変えなくても保存で修復する。
        // 修復待ちは未保存の変更ではないが、保存する意味がある状態として区別する。
        public bool HasPendingRepairs
        {
            get { return CodexEnabledInvalidFiles.Count > 0; }
        }

        // 保存ボタンを押す意味があるかどうか。未保存の変更か、修復待ちのどちらかがあるときに真になる。
        public bool NeedsSave
        {
            get { return HasChanges || HasPendingRepairs; }
        }

        public bool HasChanges
        {
            get
            {
                return DescribeChanges().Count > 0;
            }
        }

        // 読み込み時の値と現在値の差を 1 行ずつ返す。HasChanges はこの結果で決まるため、
        // 「変更あり」なのに列挙が空になることはない。
        // 保存が途中で止まると、ディスクの内容と読み込み時の値が食い違う。
        // 入力を戻しても変更なしとは言えないため、再読込か保存の成功まで変更ありとして扱う。
        // トグルを操作すると、表示上の値が読み込み時と同じでも保存結果が変わる。
        // 未保存の変更として扱わないと、確認なしに操作が失われる。
        public IReadOnlyList<string> DescribeChanges()
        {
            var changes = new List<string>();
            if (_saveInterrupted)
            {
                changes.Add("前回の保存が中断されたため、ディスクと画面の内容が食い違っている");
            }

            AddClaudeChanges(
                changes,
                _loadedImplHard,
                ImplHard,
                GetRelativePath(DefinitionKind.ClaudeHard));
            AddClaudeChanges(
                changes,
                _loadedImplStandard,
                ImplStandard,
                GetRelativePath(DefinitionKind.ClaudeStandard));
            AddClaudeChanges(
                changes,
                _loadedImplLight,
                ImplLight,
                GetRelativePath(DefinitionKind.ClaudeLight));
            AddGptChanges(
                changes,
                _loadedImplStandard,
                ImplStandard,
                GetRelativePath(DefinitionKind.GptStandard));
            AddGptChanges(
                changes,
                _loadedImplLight,
                ImplLight,
                GetRelativePath(DefinitionKind.GptLight));

            if (CodexEnabled != _loadedCodexEnabled)
            {
                changes.Add(
                    "codex_enabled: "
                    + FormatCodexEnabled(_loadedCodexEnabled)
                    + " → "
                    + FormatCodexEnabled(CodexEnabled));
            }
            else if (CodexEnabledExplicit)
            {
                changes.Add(
                    "codex_enabled: 両定義へ「"
                    + FormatCodexEnabled(CodexEnabled)
                    + "」を書き戻す");
            }

            return ReadOnly(changes);
        }

        private static void AddClaudeChanges(
            List<string> changes,
            AgentSettings loaded,
            AgentSettings current,
            string relativePath)
        {
            AddValueChange(changes, relativePath, "model", loaded.ClaudeModel, current.ClaudeModel);
            AddValueChange(changes, relativePath, "effort", loaded.ClaudeEffort, current.ClaudeEffort);
        }

        private static void AddGptChanges(
            List<string> changes,
            AgentSettings loaded,
            AgentSettings current,
            string relativePath)
        {
            AddValueChange(changes, relativePath, "codex_model", loaded.CodexModel, current.CodexModel);
            AddValueChange(
                changes,
                relativePath,
                "codex_reasoning_effort",
                loaded.CodexReasoningEffort,
                current.CodexReasoningEffort);
        }

        private static void AddValueChange(
            List<string> changes,
            string relativePath,
            string key,
            string loadedValue,
            string currentValue)
        {
            if (string.Equals(loadedValue, currentValue, StringComparison.Ordinal))
            {
                return;
            }

            changes.Add(
                relativePath
                + " の "
                + key
                + ": "
                + FormatValue(loadedValue)
                + " → "
                + FormatValue(currentValue));
        }

        private static string FormatValue(string value)
        {
            return string.IsNullOrEmpty(value) ? "(空)" : value;
        }

        private static string FormatCodexEnabled(bool value)
        {
            return value ? "有効" : "無効";
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
                    || exception is UnauthorizedAccessException
                    || exception is DecoderFallbackException)
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
            var invalidCodexEnabled = new List<string>();
            if (hasLight && ReadCodexEnabledValue(gptLight) == null)
            {
                invalidCodexEnabled.Add(GetRelativePath(DefinitionKind.GptLight));
            }

            if (hasStandard && ReadCodexEnabledValue(gptStandard) == null)
            {
                invalidCodexEnabled.Add(GetRelativePath(DefinitionKind.GptStandard));
            }

            CodexEnabledInvalidFiles = ReadOnly(invalidCodexEnabled);
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

            // 読めない定義があるまま保存すると、そのファイルの書き込みで落ちる。
            // 保存の手前で理由を返す。
            for (int i = 0; i < UnreadableFiles.Count; i++)
            {
                errors.Add("定義ファイルを読めない: " + UnreadableFiles[i]);
            }

            // 読み込み後に外部で消された定義は、書き換えないファイルでは検出できない。
            // 5 ファイルが揃っていることを保存の手前で見る。
            for (int i = 0; i < DefinitionPaths.Length; i++)
            {
                string relativePath = DefinitionPaths[i].RelativePath;
                if (IsMissing(relativePath))
                {
                    continue;
                }

                if (!File.Exists(Path.Combine(RootDirectory, relativePath)))
                {
                    errors.Add("読み込み後に定義ファイルが無くなった: " + relativePath);
                }
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
            _saveInterrupted = true;
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

            _saveInterrupted = false;
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

            // tools/codex-agent.sh は codex_reasoning_effort の省略と空値を medium として扱う。
            // 画面でも同じ既定値を補う。補わないと、正常に動く定義を開いただけで保存できなくなる。
            string effort = file.GetValue("codex_reasoning_effort");
            settings.CodexReasoningEffort = string.IsNullOrWhiteSpace(effort)
                ? DefaultGptEffort
                : effort;
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

        // 値は FrontMatterFile が二重引用符で囲んで書くため、YAML の予約語や数値でも文字列として読まれる。
        // 使える文字を絞るのは、tools/codex-agent.sh の fm_get が外側の引用符を外すだけで
        // エスケープを戻さないためである。\ と " を通さない限り、スクリプトと設定コンソールの読みは一致する。
        private static bool IsSafeScalar(string value)
        {
            if (value.Length == 0)
            {
                return false;
            }

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
            // 不正値が書かれている定義は、保存のたびに正しい値へ直す。
            // 直さないとスクリプトが終了コード 2 で止まり続けるためである。
            // 直す対象は不正値を持つ定義だけとする。正常なもう片方を巻き添えにしないためである。
            if (CodexEnabled != _loadedCodexEnabled
                || CodexEnabledExplicit
                || IsCodexEnabledInvalid(kind))
            {
                SetCodexEnabled(file, CodexEnabled);
            }
            // codex_model が無い定義は、GPT 経路が無効のときだけ保存を通る(スクリプトは無効判定を先に行う)。
            // 値が空でキーも無いなら書かない。空のキーを足しても定義は有効にならず、変えていないファイルを書き換えるだけになる。
            string codexModel = settings.CodexModel ?? string.Empty;
            string existingCodexModel;
            if (codexModel.Length > 0 || file.TryGetValue("codex_model", out existingCodexModel))
            {
                file.SetValue("codex_model", codexModel);
            }

            // 省略された codex_reasoning_effort は medium として読む。
            // 画面で変えていないのにキーを足すと、値が変わっていないファイルを書き換えることになる。
            string currentEffort;
            bool hasEffort = file.TryGetValue("codex_reasoning_effort", out currentEffort);
            bool effortIsDefault = string.Equals(
                settings.CodexReasoningEffort,
                DefaultGptEffort,
                StringComparison.Ordinal);
            if (hasEffort || !effortIsDefault)
            {
                file.SetValue("codex_reasoning_effort", settings.CodexReasoningEffort);
            }
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

        // 保存で codex_enabled を揃えた、または直した場合に、警告の表示を残さないため読み直す。
        private void RefreshCodexEnabledMismatch()
        {
            FrontMatterFile gptLight;
            FrontMatterFile gptStandard;
            bool hasLight = _files.TryGetValue(GetRelativePath(DefinitionKind.GptLight), out gptLight);
            bool hasStandard = _files.TryGetValue(GetRelativePath(DefinitionKind.GptStandard), out gptStandard);
            CodexEnabledMismatch = hasLight
                && hasStandard
                && ReadEffectiveCodexEnabled(gptLight) != ReadEffectiveCodexEnabled(gptStandard);

            var invalidCodexEnabled = new List<string>();
            if (hasLight && ReadCodexEnabledValue(gptLight) == null)
            {
                invalidCodexEnabled.Add(GetRelativePath(DefinitionKind.GptLight));
            }

            if (hasStandard && ReadCodexEnabledValue(gptStandard) == null)
            {
                invalidCodexEnabled.Add(GetRelativePath(DefinitionKind.GptStandard));
            }

            CodexEnabledInvalidFiles = ReadOnly(invalidCodexEnabled);
        }

        private FrontMatterFile GetFile(DefinitionKind kind)
        {
            return _files[GetRelativePath(kind)];
        }

        // codex_enabled の読み方は tools/codex-agent.sh に合わせる。
        // キーが無ければ true、true と false はそのまま、それ以外は不正値である。
        // スクリプトは不正値で終了コード 2 に倒れるため、有効として表示してはならない。
        private static bool? ReadCodexEnabledValue(FrontMatterFile file)
        {
            string value;
            if (!file.TryGetValue(CodexEnabledKey, out value))
            {
                return true;
            }

            if (string.Equals(value, "true", StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(value, "false", StringComparison.Ordinal))
            {
                return false;
            }

            return null;
        }

        private bool IsCodexEnabledInvalid(DefinitionKind kind)
        {
            string relativePath = GetRelativePath(kind);
            for (int i = 0; i < CodexEnabledInvalidFiles.Count; i++)
            {
                if (string.Equals(CodexEnabledInvalidFiles[i], relativePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsMissing(string relativePath)
        {
            for (int i = 0; i < MissingFiles.Count; i++)
            {
                if (string.Equals(MissingFiles[i], relativePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ReadEffectiveCodexEnabled(FrontMatterFile file)
        {
            // 不正値は安全側の無効として扱う。
            return ReadCodexEnabledValue(file) == true;
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
            _saveInterrupted = false;
        }

        private static string GetDefaultRootDirectory()
        {
            return Path.Combine(GetUserProfile(), ".claude");
        }

        // tools/codex-agent.sh は cygpath で /d/... を D:/... へ直す。
        // 同じ値を同じパスとして扱わないと、存在確認と CODEX_HOME がスクリプトとずれる。
        private static string ConvertMsysPath(string value)
        {
            if (value.Length < 2 || value[0] != '/')
            {
                return value;
            }

            char drive = value[1];
            bool isDriveLetter = (drive >= 'A' && drive <= 'Z') || (drive >= 'a' && drive <= 'z');
            if (!isDriveLetter)
            {
                return value;
            }

            if (value.Length == 2)
            {
                return char.ToUpperInvariant(drive) + ":\\";
            }

            if (value[2] != '/')
            {
                return value;
            }

            return char.ToUpperInvariant(drive) + ":\\" + value.Substring(3).Replace('/', '\\');
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
            expanded = ConvertMsysPath(expanded);

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

        internal void CopyValuesFrom(AgentSettings other)
        {
            ClaudeModel = other.ClaudeModel;
            ClaudeEffort = other.ClaudeEffort;
            CodexModel = other.CodexModel;
            CodexReasoningEffort = other.CodexReasoningEffort;
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
