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
        private readonly string _homeDirectory;
        private string _codexHome;
        private string _loadedCodexHome;
        private AgentSettings _loadedImplHard;
        private AgentSettings _loadedImplStandard;
        private AgentSettings _loadedImplLight;
        private bool _loadedCodexEnabled;
        private bool _saveInterrupted;

        public ConsoleSettings()
            : this(null, null)
        {
        }

        public ConsoleSettings(string rootDirectory)
            : this(rootDirectory, null)
        {
        }

        // homeDirectory は codex_home の ~ を展開する先であり、認証ホームの一覧を探す親でもある。
        // 省略時は %USERPROFILE% を使う。実在のホームに依存せず動かせるよう、外から差し替えられるようにしている。
        public ConsoleSettings(string rootDirectory, string homeDirectory)
        {
            _homeDirectory = string.IsNullOrEmpty(homeDirectory) ? GetUserProfile() : homeDirectory;
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

        // 選択中の認証ホーム。impl-light と impl-standard の codex_home を 1 つの設定として扱う。
        // 値の形は定義ファイルと同じ ~/<ディレクトリ名> である。
        public string CodexHome
        {
            get { return _codexHome; }

            set
            {
                _codexHome = value;
                ExpandedCodexHome = ExpandCodexHome(value);
                CodexHomeExists = !string.IsNullOrEmpty(ExpandedCodexHome)
                    && Directory.Exists(ExpandedCodexHome);
                CodexHomeIsListed = IsListedCodexHome(value);
            }
        }

        // ホームディレクトリ直下に実在する .codex で始まるディレクトリを ~/<名前> の形で並べたもの。
        public IReadOnlyList<string> CodexHomeChoices { get; private set; }

        // 選択中の値が一覧にあるかどうか。一覧に無い値は表示するだけで、保存では書き換えない。
        public bool CodexHomeIsListed { get; private set; }

        // impl-light と impl-standard の codex_home が食い違うかどうか。
        public bool CodexHomeMismatch { get; private set; }

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

        // codex_enabled の不正値と codex_home の食い違いは、利用者が何も変えなくても保存で直す。
        // 修復待ちは未保存の変更ではないが、保存する意味がある状態として区別する。
        public bool HasPendingRepairs
        {
            get { return CodexEnabledInvalidFiles.Count > 0 || NeedsCodexHomeAlignment; }
        }

        // 食い違った codex_home を選択中の値で揃えられる状態。
        // 一覧に無い値は書き換えないため、その場合は揃えられない。
        public bool NeedsCodexHomeAlignment
        {
            get { return CodexHomeMismatch && CodexHomeIsListed; }
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

            if (!string.Equals(_loadedCodexHome, CodexHome, StringComparison.Ordinal))
            {
                changes.Add(
                    "codex_home: "
                    + FormatValue(_loadedCodexHome)
                    + " → "
                    + FormatValue(CodexHome));
            }

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

        // 入力中の値を保持したまま読み直す。編集した項目だけを残し、編集していない項目は読み直した値にする。
        // 全項目を戻すと、外部で変えられた未編集の項目まで次の保存で旧値に戻してしまう。
        // 編集した項目が外部でも変えられていた場合は入力中の値を優先し、その項目を戻り値で知らせる。
        public IReadOnlyList<string> ReloadPreservingEdits()
        {
            AgentSettings editedHard = ImplHard.Clone();
            AgentSettings editedStandard = ImplStandard.Clone();
            AgentSettings editedLight = ImplLight.Clone();
            AgentSettings previousHard = _loadedImplHard.Clone();
            AgentSettings previousStandard = _loadedImplStandard.Clone();
            AgentSettings previousLight = _loadedImplLight.Clone();
            bool editedCodexEnabled = CodexEnabled;
            bool codexEnabledEdited = CodexEnabledExplicit || CodexEnabled != _loadedCodexEnabled;
            string editedCodexHome = CodexHome;
            string previousCodexHome = _loadedCodexHome;

            // codex_enabled は 2 定義の集約値を画面に出すが、外部変更の検出は定義ごとに行う。
            // 集約値だけを比べると、片方だけが外部で変わった場合を見逃す。
            bool? previousStandardEnabled = ReadFileCodexEnabled(DefinitionKind.GptStandard);
            bool? previousLightEnabled = ReadFileCodexEnabled(DefinitionKind.GptLight);
            string previousStandardCodexHome = ReadFileCodexHome(DefinitionKind.GptStandard);

            Reload();

            var conflicts = new List<string>();
            RestoreClaudeEdits(ImplHard, editedHard, previousHard, GetRelativePath(DefinitionKind.ClaudeHard), conflicts);
            RestoreClaudeEdits(ImplStandard, editedStandard, previousStandard, GetRelativePath(DefinitionKind.ClaudeStandard), conflicts);
            RestoreClaudeEdits(ImplLight, editedLight, previousLight, GetRelativePath(DefinitionKind.ClaudeLight), conflicts);
            RestoreGptEdits(ImplStandard, editedStandard, previousStandard, GetRelativePath(DefinitionKind.GptStandard), conflicts);
            RestoreGptEdits(ImplLight, editedLight, previousLight, GetRelativePath(DefinitionKind.GptLight), conflicts);

            // codex_home は impl-light の値を代表として表示するため、競合もその定義名で知らせる。
            CodexHome = RestoreEdit(
                CodexHome,
                editedCodexHome,
                previousCodexHome,
                GetRelativePath(DefinitionKind.GptLight),
                CodexHomeKey,
                conflicts);

            // impl-standard 側の外部変更は代表値との比較では見えないため、定義ごとに見る。
            // 保存では選択値を両定義へ書くので、知らせずに上書きしてはならない。
            AddCodexHomeConflict(
                conflicts, DefinitionKind.GptStandard, previousStandardCodexHome, editedCodexHome, previousCodexHome);

            if (codexEnabledEdited)
            {
                AddCodexEnabledConflict(
                    conflicts, DefinitionKind.GptStandard, previousStandardEnabled, editedCodexEnabled);
                AddCodexEnabledConflict(
                    conflicts, DefinitionKind.GptLight, previousLightEnabled, editedCodexEnabled);
                CodexEnabled = editedCodexEnabled;
                CodexEnabledExplicit = true;
            }

            return ReadOnly(conflicts);
        }

        // 定義の codex_home が外部で変わり、入力中の値とも違うときだけ競合として知らせる。
        // 入力中の値が読み込み時から変わっていなければ、外部変更はそのまま反映されるので知らせない。
        private void AddCodexHomeConflict(
            List<string> conflicts,
            DefinitionKind kind,
            string previous,
            string edited,
            string previousSelected)
        {
            if (string.Equals(edited, previousSelected, StringComparison.Ordinal))
            {
                return;
            }

            string reloaded = ReadFileCodexHome(kind);
            if (previous != null
                && reloaded != null
                && !string.Equals(reloaded, previous, StringComparison.Ordinal)
                && !string.Equals(reloaded, edited, StringComparison.Ordinal))
            {
                conflicts.Add(
                    GetRelativePath(kind) + " の " + CodexHomeKey + ": 外部で " + FormatValue(reloaded)
                    + " に変わったが、入力中の " + FormatValue(edited) + " を優先する");
            }
        }

        // 定義の codex_enabled が外部で変わり、入力中の値とも違うときだけ競合として知らせる。
        private void AddCodexEnabledConflict(
            List<string> conflicts,
            DefinitionKind kind,
            bool? previous,
            bool edited)
        {
            bool? reloaded = ReadFileCodexEnabled(kind);
            if (previous.HasValue && reloaded.HasValue && reloaded != previous && reloaded != edited)
            {
                conflicts.Add(
                    GetRelativePath(kind) + " の codex_enabled: 外部で " + FormatCodexEnabled(reloaded.Value)
                    + " に変わったが、入力中の " + FormatCodexEnabled(edited) + " を優先する");
            }
        }

        private static void RestoreClaudeEdits(
            AgentSettings target,
            AgentSettings edited,
            AgentSettings previous,
            string relativePath,
            List<string> conflicts)
        {
            target.ClaudeModel = RestoreEdit(
                target.ClaudeModel, edited.ClaudeModel, previous.ClaudeModel, relativePath, "model", conflicts);
            target.ClaudeEffort = RestoreEdit(
                target.ClaudeEffort, edited.ClaudeEffort, previous.ClaudeEffort, relativePath, "effort", conflicts);
        }

        private static void RestoreGptEdits(
            AgentSettings target,
            AgentSettings edited,
            AgentSettings previous,
            string relativePath,
            List<string> conflicts)
        {
            target.CodexModel = RestoreEdit(
                target.CodexModel, edited.CodexModel, previous.CodexModel, relativePath, "codex_model", conflicts);
            target.CodexReasoningEffort = RestoreEdit(
                target.CodexReasoningEffort,
                edited.CodexReasoningEffort,
                previous.CodexReasoningEffort,
                relativePath,
                "codex_reasoning_effort",
                conflicts);
        }

        // 編集されていない項目は読み直した値を返す。編集された項目は入力中の値を返す。
        private static string RestoreEdit(
            string reloaded,
            string edited,
            string previous,
            string relativePath,
            string key,
            List<string> conflicts)
        {
            if (string.Equals(edited, previous, StringComparison.Ordinal))
            {
                return reloaded;
            }

            if (!string.Equals(reloaded, previous, StringComparison.Ordinal)
                && !string.Equals(reloaded, edited, StringComparison.Ordinal))
            {
                conflicts.Add(
                    relativePath + " の " + key + ": 外部で " + FormatValue(reloaded)
                    + " に変わったが、入力中の " + FormatValue(edited) + " を優先する");
            }

            return edited;
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

            // 選択肢は CodexHome より先に決める。CodexHome の設定子が一覧との照合を行うためである。
            CodexHomeChoices = EnumerateCodexHomes(_homeDirectory);
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

            RefreshCodexHomeMismatch();

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

            // 選択肢は読み込み時に列挙したものなので、保存の手前で認証ホームがまだ実在するかを見る。
            // 消えたホームを書くと、次のサブエージェント起動が認証ホーム不足で止まる。
            if (ShouldWriteCodexHome() && !Directory.Exists(ExpandedCodexHome))
            {
                errors.Add("認証ホームが存在しない: " + FormatValue(CodexHome));
            }

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

                        // 書き込めたファイルの分だけ基準値をディスクに合わせる。
                        // 後続で中断しても、書き込み済みの値を「読み込み時のまま」と誤認しないためである。
                        // 誤認すると、書き込み前の値へ戻す編集が未編集と判定され、入力を保持する再読込で失われる。
                        MarkSaved(definition.Kind);
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
            RefreshCodexHomeMismatch();
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

            // codex_home は 2 定義を 1 つの設定として扱うため、選択中の値を両方へ書く。
            if (ShouldWriteCodexHome())
            {
                file.SetValue(CodexHomeKey, CodexHome);
            }

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

        // 一覧に無い値は書き換えない。$USERPROFILE 形式や未設定など、
        // 設定コンソールが組み立てていない値を勝手に別の形へ直さないためである。
        // 一覧の値は EnumerateCodexHomes が \ と " を含まないディレクトリ名だけから組み立てるため、
        // 二重引用符で囲んで書いても tools/codex-agent.sh の fm_get が同じ値として読む。
        private bool ShouldWriteCodexHome()
        {
            if (!CodexHomeIsListed)
            {
                return false;
            }

            // 食い違っているときは、選択中の値を変えていなくても両定義を揃える。
            return CodexHomeMismatch
                || !string.Equals(CodexHome, _loadedCodexHome, StringComparison.Ordinal);
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

        // 保存で codex_home を揃えた場合に、警告の表示を残さないため読み直す。
        // 画面に出す値は impl-light の側であり、impl-standard がそれと違えば食い違いとする。
        private void RefreshCodexHomeMismatch()
        {
            string light = ReadFileCodexHome(DefinitionKind.GptLight);
            string standard = ReadFileCodexHome(DefinitionKind.GptStandard);
            CodexHomeMismatch = light != null
                && standard != null
                && !string.Equals(light, standard, StringComparison.Ordinal);
        }

        // 定義ごとの codex_home。ファイルが無いときは null を返し、キーが無いときは空文字を返す。
        // キーの有無を値の違いとして扱わないと、片方だけ codex_home を持つ状態を見逃す。
        private string ReadFileCodexHome(DefinitionKind kind)
        {
            FrontMatterFile file;
            if (!_files.TryGetValue(GetRelativePath(kind), out file))
            {
                return null;
            }

            return file.GetValue(CodexHomeKey) ?? string.Empty;
        }

        private bool IsListedCodexHome(string value)
        {
            if (string.IsNullOrEmpty(value) || CodexHomeChoices == null)
            {
                return false;
            }

            for (int i = 0; i < CodexHomeChoices.Count; i++)
            {
                if (string.Equals(CodexHomeChoices[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // ホームディレクトリ直下の .codex で始まるディレクトリを名前順に並べ、~/<名前> の形で返す。
        // 名前に英数字と . _ - / 以外を含むものは除く。二重引用符で囲んで書いた値を
        // tools/codex-agent.sh の fm_get が同じ値として読めない場合があるためである。
        private static IReadOnlyList<string> EnumerateCodexHomes(string homeDirectory)
        {
            var names = new List<string>();
            if (!string.IsNullOrEmpty(homeDirectory) && Directory.Exists(homeDirectory))
            {
                try
                {
                    foreach (string path in Directory.GetDirectories(homeDirectory))
                    {
                        string name = Path.GetFileName(path);
                        if (name.StartsWith(".codex", StringComparison.OrdinalIgnoreCase)
                            && IsSafeScalar(name))
                        {
                            names.Add(name);
                        }
                    }
                }
                catch (Exception exception) when (
                    exception is IOException || exception is UnauthorizedAccessException)
                {
                    // 一覧を取れない環境でも画面は開く。定義に書かれた値は一覧に無い値として表示される。
                    names.Clear();
                }
            }

            names.Sort(StringComparer.Ordinal);
            var choices = new List<string>(names.Count);
            for (int i = 0; i < names.Count; i++)
            {
                choices.Add("~/" + names[i]);
            }

            return ReadOnly(choices);
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

        private void MarkSaved(DefinitionKind kind)
        {
            switch (kind)
            {
                case DefinitionKind.ClaudeHard:
                    CopyClaudeValues(ImplHard, _loadedImplHard);
                    break;
                case DefinitionKind.ClaudeStandard:
                    CopyClaudeValues(ImplStandard, _loadedImplStandard);
                    break;
                case DefinitionKind.ClaudeLight:
                    CopyClaudeValues(ImplLight, _loadedImplLight);
                    break;
                case DefinitionKind.GptStandard:
                    CopyGptValues(ImplStandard, _loadedImplStandard);
                    break;
                case DefinitionKind.GptLight:
                    CopyGptValues(ImplLight, _loadedImplLight);
                    break;
            }
        }

        private static void CopyClaudeValues(AgentSettings source, AgentSettings target)
        {
            target.ClaudeModel = source.ClaudeModel;
            target.ClaudeEffort = source.ClaudeEffort;
        }

        private static void CopyGptValues(AgentSettings source, AgentSettings target)
        {
            target.CodexModel = source.CodexModel;
            target.CodexReasoningEffort = source.CodexReasoningEffort;
        }

        // 定義ごとの codex_enabled の実効値。ファイルが無いときは null を返す。
        private bool? ReadFileCodexEnabled(DefinitionKind kind)
        {
            FrontMatterFile file;
            if (_files.TryGetValue(GetRelativePath(kind), out file))
            {
                return ReadEffectiveCodexEnabled(file);
            }

            return null;
        }

        private void SaveLoadedValues()
        {
            _loadedImplHard = ImplHard.Clone();
            _loadedImplStandard = ImplStandard.Clone();
            _loadedImplLight = ImplLight.Clone();
            _loadedCodexEnabled = CodexEnabled;
            _loadedCodexHome = CodexHome;
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

        private string ExpandCodexHome(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            string userProfile = _homeDirectory;
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
