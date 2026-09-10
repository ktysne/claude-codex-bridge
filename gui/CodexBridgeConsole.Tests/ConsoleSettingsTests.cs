using System;
using System.Collections.Generic;
using System.IO;
using CodexBridgeConsole;
using Xunit;

namespace CodexBridgeConsole.Tests
{
    public sealed class ConsoleSettingsTests
    {
        private const string ClaudeHardPath = "agents\\impl-hard.md";
        private const string ClaudeStandardPath = "agents\\impl-standard.md";
        private const string ClaudeLightPath = "agents\\impl-light.md";
        private const string GptStandardPath = "gpt-agents\\impl-standard.md";
        private const string GptLightPath = "gpt-agents\\impl-light.md";

        private static readonly string[] DefinitionPaths =
        {
            ClaudeHardPath,
            ClaudeStandardPath,
            ClaudeLightPath,
            GptStandardPath,
            GptLightPath
        };

        [Fact]
        public void Load_ReadsClaudeAndCodexValuesFromAllDefinitions()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);

                var settings = CreateSettings(directory);

                Assert.Equal("claude-hard-model", settings.ImplHard.ClaudeModel);
                Assert.Equal("high", settings.ImplHard.ClaudeEffort);
                Assert.Equal("claude-standard-model", settings.ImplStandard.ClaudeModel);
                Assert.Equal("medium", settings.ImplStandard.ClaudeEffort);
                Assert.Equal("codex-standard-model", settings.ImplStandard.CodexModel);
                Assert.Equal("xhigh", settings.ImplStandard.CodexReasoningEffort);
                Assert.Equal("claude-light-model", settings.ImplLight.ClaudeModel);
                Assert.Equal("low", settings.ImplLight.ClaudeEffort);
                Assert.Equal("codex-light-model", settings.ImplLight.CodexModel);
                Assert.Equal("high", settings.ImplLight.CodexReasoningEffort);
                Assert.Equal("~/.codex", settings.CodexHome);
                Assert.Equal("workspace-write", settings.CodexSandbox);
            }
        }

        [Fact]
        public void Load_ReportsMissingDefinitionAndDisablesSaving()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                File.Delete(GetPath(directory, ClaudeHardPath));

                var settings = CreateSettings(directory);

                Assert.Single(settings.MissingFiles);
                Assert.Equal(ClaudeHardPath, settings.MissingFiles[0]);
                Assert.False(settings.CanSave);
            }
        }

        [Fact]
        public void Load_TreatsMissingCodexEnabledAsTrue()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);

                var settings = CreateSettings(directory);

                Assert.True(settings.CodexEnabled);
            }
        }

        [Fact]
        public void Save_DisablingCodexAddsFalseToBothCodexDefinitions()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                ConsoleSettings settings = CreateSettings(directory);
                settings.CodexEnabled = false;

                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Contains(GptStandardPath, result.ChangedFiles);
                Assert.Contains(GptLightPath, result.ChangedFiles);
                Assert.Contains("codex_enabled: \"false\"", ReadDefinition(directory, GptStandardPath));
                Assert.Contains("codex_enabled: \"false\"", ReadDefinition(directory, GptLightPath));
            }
        }

        [Fact]
        public void Save_WithoutChangesDoesNotRewriteMissingCodexEnabledKeys()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);
                byte[] standardBefore = File.ReadAllBytes(GetPath(directory, GptStandardPath));
                byte[] lightBefore = File.ReadAllBytes(GetPath(directory, GptLightPath));

                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.DoesNotContain(GptStandardPath, result.ChangedFiles);
                Assert.DoesNotContain(GptLightPath, result.ChangedFiles);
                Assert.Equal(standardBefore, File.ReadAllBytes(GetPath(directory, GptStandardPath)));
                Assert.Equal(lightBefore, File.ReadAllBytes(GetPath(directory, GptLightPath)));
            }
        }

        [Fact]
        public void Save_EnablingCodexChangesFalseToTrueInBothCodexDefinitions()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory, false);
                ConsoleSettings settings = CreateSettings(directory);
                settings.CodexEnabled = true;

                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Contains(GptStandardPath, result.ChangedFiles);
                Assert.Contains(GptLightPath, result.ChangedFiles);
                Assert.Contains("codex_enabled: \"true\"", ReadDefinition(directory, GptStandardPath));
                Assert.Contains("codex_enabled: \"true\"", ReadDefinition(directory, GptLightPath));
            }
        }

        [Fact]
        public void Save_ChangingOneClaudeModelOnlyChangesItsDefinition()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var originals = new Dictionary<string, byte[]>();
                for (int i = 0; i < DefinitionPaths.Length; i++)
                {
                    originals.Add(
                        DefinitionPaths[i],
                        File.ReadAllBytes(GetPath(directory, DefinitionPaths[i])));
                }

                var settings = CreateSettings(directory);
                settings.ImplHard.ClaudeModel = "claude-hard-model-updated";
                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Single(result.ChangedFiles);
                Assert.Equal(ClaudeHardPath, result.ChangedFiles[0]);
                Assert.Contains(
                    "model: \"claude-hard-model-updated\"",
                    ReadDefinition(directory, ClaudeHardPath));
                for (int i = 0; i < DefinitionPaths.Length; i++)
                {
                    if (DefinitionPaths[i] == ClaudeHardPath)
                    {
                        continue;
                    }

                    Assert.Equal(
                        originals[DefinitionPaths[i]],
                        File.ReadAllBytes(GetPath(directory, DefinitionPaths[i])));
                }
            }
        }

        [Theory]
        [InlineData("claude-model")]
        [InlineData("claude-effort")]
        [InlineData("codex-model")]
        [InlineData("codex-reasoning-effort")]
        public void Save_ReturnsReasonForInvalidSetting(string invalidSetting)
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);
                string expectedError;
                switch (invalidSetting)
                {
                    case "claude-model":
                        settings.ImplHard.ClaudeModel = string.Empty;
                        expectedError = ClaudeHardPath + " の model が空である";
                        break;
                    case "claude-effort":
                        settings.ImplHard.ClaudeEffort = string.Empty;
                        expectedError = ClaudeHardPath + " の effort が空である";
                        break;
                    case "codex-model":
                        settings.ImplStandard.CodexModel = string.Empty;
                        expectedError = GptStandardPath + " の codex_model が空である";
                        break;
                    case "codex-reasoning-effort":
                        settings.ImplStandard.CodexReasoningEffort = "invalid";
                        expectedError = GptStandardPath + " の codex_reasoning_effort が不正である: invalid";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(invalidSetting), invalidSetting, null);
                }

                ConsoleSettingsSaveResult result = settings.Save();

                Assert.False(result.Succeeded);
                Assert.Contains(expectedError, result.ValidationErrors[0]);
            }
        }

        [Fact]
        public void Validate_WhenCodexIsDisabledAllowsEmptyCodexModels()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                ConsoleSettings settings = CreateSettings(directory);
                settings.CodexEnabled = false;
                settings.ImplStandard.CodexModel = string.Empty;
                settings.ImplLight.CodexModel = string.Empty;

                Assert.Empty(settings.Validate());
            }
        }

        [Fact]
        public void Reload_DiscardsUnsavedChanges()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);
                settings.ImplStandard.ClaudeModel = "未保存のモデル";
                settings.CodexEnabled = false;

                Assert.True(settings.HasChanges);

                settings.Reload();

                Assert.Equal("claude-standard-model", settings.ImplStandard.ClaudeModel);
                Assert.True(settings.CodexEnabled);
                Assert.False(settings.HasChanges);
            }
        }

        [Fact]
        public void DescribeChanges_ReturnsEmptyWhenNothingChanged()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);

                Assert.Empty(settings.DescribeChanges());
            }
        }

        [Fact]
        public void DescribeChanges_ReportsChangedClaudeModel()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);
                settings.ImplHard.ClaudeModel = "claude-hard-model-updated";

                Assert.Equal(
                    new[]
                    {
                        ClaudeHardPath
                            + " の model: claude-hard-model → claude-hard-model-updated"
                    },
                    settings.DescribeChanges());
            }
        }

        [Fact]
        public void DescribeChanges_ReportsExplicitCodexEnabledWritebackWhenValueIsSame()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);
                settings.CodexEnabledExplicit = true;

                Assert.Equal(
                    new[] { "codex_enabled: 両定義へ「有効」を書き戻す" },
                    settings.DescribeChanges());
            }
        }

        [Fact]
        public void ReloadPreservingEdits_KeepsEditedValueAndTakesExternalChangeForOthers()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory, true);
                var settings = CreateSettings(directory);
                settings.ImplHard.ClaudeModel = "claude-hard-model-edited";

                // 利用者が触っていない項目を外部で変える。
                WriteDefinition(
                    directory,
                    ClaudeLightPath,
                    ClaudeDefinition("impl-light", "claude-light-model-external", "low"));
                WriteDefinition(directory, GptStandardPath, GptDefinition("codex-standard-model", "xhigh", false));
                WriteDefinition(directory, GptLightPath, GptDefinition("codex-light-model", "high", false));

                IReadOnlyList<string> conflicts = settings.ReloadPreservingEdits();

                Assert.Empty(conflicts);
                Assert.Equal("claude-hard-model-edited", settings.ImplHard.ClaudeModel);
                Assert.Equal("claude-light-model-external", settings.ImplLight.ClaudeModel);
                Assert.False(settings.CodexEnabled);
                Assert.False(settings.CodexEnabledExplicit);
                Assert.Equal(
                    new[] { ClaudeHardPath + " の model: claude-hard-model → claude-hard-model-edited" },
                    settings.DescribeChanges());
            }
        }

        [Fact]
        public void ReloadPreservingEdits_PrefersEditedValueAndReportsConflict()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);
                settings.ImplHard.ClaudeModel = "claude-hard-model-edited";
                WriteDefinition(
                    directory,
                    ClaudeHardPath,
                    ClaudeDefinition("impl-hard", "claude-hard-model-external", "high"));

                IReadOnlyList<string> conflicts = settings.ReloadPreservingEdits();

                Assert.Equal(
                    new[]
                    {
                        ClaudeHardPath
                            + " の model: 外部で claude-hard-model-external に変わったが、入力中の claude-hard-model-edited を優先する"
                    },
                    conflicts);
                Assert.Equal("claude-hard-model-edited", settings.ImplHard.ClaudeModel);
                Assert.True(settings.HasChanges);
            }
        }

        [Fact]
        public void ReloadPreservingEdits_KeepsExplicitCodexEnabledToggle()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory, true);
                var settings = CreateSettings(directory);
                settings.CodexEnabled = false;
                settings.CodexEnabledExplicit = true;

                settings.ReloadPreservingEdits();

                Assert.False(settings.CodexEnabled);
                Assert.True(settings.CodexEnabledExplicit);
                Assert.True(settings.HasChanges);
            }
        }

        [Fact]
        public void ReloadPreservingEdits_KeepsRevertAfterInterruptedSave()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);

                // 先頭のファイルだけ保存され、最後のファイルで中断する状態を作る。
                WriteDefinition(
                    directory,
                    GptLightPath,
                    GptDefinition("codex-light-model-external", "high", null));
                settings.ImplHard.ClaudeModel = "claude-hard-model-updated";
                settings.ImplLight.CodexModel = "codex-light-model-updated";
                Assert.Throws<FrontMatterFileChangedException>(() => settings.Save());

                // 書き込み済みの値を元に戻す編集は、未編集ではなく差分として扱われる。
                settings.ImplHard.ClaudeModel = "claude-hard-model";
                Assert.Contains(
                    ClaudeHardPath + " の model: claude-hard-model-updated → claude-hard-model",
                    settings.DescribeChanges());

                IReadOnlyList<string> conflicts = settings.ReloadPreservingEdits();

                // 戻した値が保持され、外部と重なった light の codex_model だけが競合として報告される。
                Assert.Equal(
                    new[]
                    {
                        GptLightPath
                            + " の codex_model: 外部で codex-light-model-external に変わったが、入力中の codex-light-model-updated を優先する"
                    },
                    conflicts);
                Assert.Equal("claude-hard-model", settings.ImplHard.ClaudeModel);
                Assert.Equal("codex-light-model-updated", settings.ImplLight.CodexModel);
                Assert.True(settings.HasChanges);
            }
        }

        [Fact]
        public void ReloadPreservingEdits_ReportsCodexEnabledConflictPerDefinition()
        {
            using (var directory = new TemporaryDirectory())
            {
                // standard=false / light=true の食い違いから始め、画面で有効にする。
                WriteMismatchedDefinitions(directory);
                var settings = CreateSettings(directory);
                settings.CodexEnabled = true;
                settings.CodexEnabledExplicit = true;

                // 外部が light だけを無効にする。集約値は無効のまま変わらない。
                WriteDefinition(directory, GptLightPath, GptDefinition("codex-light-model", "high", false));

                IReadOnlyList<string> conflicts = settings.ReloadPreservingEdits();

                Assert.Equal(
                    new[] { GptLightPath + " の codex_enabled: 外部で 無効 に変わったが、入力中の 有効 を優先する" },
                    conflicts);
                Assert.True(settings.CodexEnabled);
                Assert.True(settings.CodexEnabledExplicit);
            }
        }

        [Fact]
        public void Save_ReportsAlreadySavedFilesWhenInterrupted()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);

                // 保存は agents\impl-hard.md から gpt-agents\impl-light.md の順に行う。
                // 最後のファイルを外部から書き換え、先頭のファイルだけが保存された状態を作る。
                WriteDefinition(
                    directory,
                    GptLightPath,
                    GptDefinition("codex-light-model-external", "high", null));
                settings.ImplHard.ClaudeModel = "claude-hard-model-updated";
                settings.ImplLight.CodexModel = "codex-light-model-updated";

                Assert.Throws<FrontMatterFileChangedException>(() => settings.Save());

                Assert.Contains(ClaudeHardPath, settings.LastChangedFiles);
                Assert.Contains("claude-hard-model-updated", ReadDefinition(directory, ClaudeHardPath));
                Assert.Contains("codex-light-model-external", ReadDefinition(directory, GptLightPath));
            }
        }

        [Fact]
        public void Load_TreatsMismatchedCodexEnabledAsDisabled()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteMismatchedDefinitions(directory);

                var settings = CreateSettings(directory);

                Assert.False(settings.CodexEnabled);
                Assert.True(settings.CodexEnabledMismatch);
            }
        }

        [Fact]
        public void Save_KeepsMismatchedCodexEnabledWhenToggleIsNotChanged()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteMismatchedDefinitions(directory);
                var settings = CreateSettings(directory);

                settings.ImplHard.ClaudeModel = "claude-hard-model-updated";
                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Equal(new[] { ClaudeHardPath }, result.ChangedFiles);
                Assert.Contains("codex_enabled: false", ReadDefinition(directory, GptStandardPath));
                Assert.Contains("codex_enabled: true", ReadDefinition(directory, GptLightPath));
            }
        }

        [Fact]
        public void Save_WritesSameCodexEnabledToBothWhenToggleIsChanged()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteMismatchedDefinitions(directory);
                var settings = CreateSettings(directory);

                settings.CodexEnabled = true;
                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Contains(GptStandardPath, result.ChangedFiles);
                Assert.Contains("codex_enabled: \"true\"", ReadDefinition(directory, GptStandardPath));
                // light はすでに true であるため書き換えず、引用符の付かない元の行が残る。
                Assert.Contains("codex_enabled: true\n", ReadDefinition(directory, GptLightPath));
            }
        }

        [Fact]
        public void Save_NormalizesMismatchedCodexEnabledWhenToggleIsOperated()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteMismatchedDefinitions(directory);
                var settings = CreateSettings(directory);

                // 食い違いは無効として表示するため、両方を無効にする操作は表示上の値が変わらない。
                // 画面がトグルを操作したことを伝えると、表示どおりの値を両方へ書き戻す。
                Assert.False(settings.CodexEnabled);
                settings.CodexEnabledExplicit = true;
                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Contains(GptLightPath, result.ChangedFiles);
                // standard はすでに false であるため書き換えず、引用符の付かない元の行が残る。
                Assert.Contains("codex_enabled: false\n", ReadDefinition(directory, GptStandardPath));
                Assert.Contains("codex_enabled: \"false\"", ReadDefinition(directory, GptLightPath));
            }
        }

        [Fact]
        public void HasChanges_IsTrueWhenToggleIsOperatedOnMismatch()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteMismatchedDefinitions(directory);
                var settings = CreateSettings(directory);

                Assert.False(settings.HasChanges);
                settings.CodexEnabledExplicit = true;

                Assert.True(settings.HasChanges);
            }
        }

        [Fact]
        public void Save_ClearsMismatchAfterNormalizing()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteMismatchedDefinitions(directory);
                var settings = CreateSettings(directory);
                settings.CodexEnabledExplicit = true;

                Assert.True(settings.Save().Succeeded);

                Assert.False(settings.CodexEnabledMismatch);
                Assert.False(settings.HasChanges);
            }
        }

        [Fact]
        public void Save_ReportsAlreadySavedFilesWhenWriteFails()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);
                string blockedPath = GetPath(directory, GptLightPath);
                File.SetAttributes(blockedPath, FileAttributes.ReadOnly);
                try
                {
                    settings.ImplHard.ClaudeModel = "claude-hard-model-updated";
                    settings.ImplLight.CodexModel = "codex-light-model-updated";

                    Assert.ThrowsAny<Exception>(() => settings.Save());

                    Assert.Equal(new[] { ClaudeHardPath }, settings.LastChangedFiles);
                    Assert.Contains("claude-hard-model-updated", ReadDefinition(directory, ClaudeHardPath));
                }
                finally
                {
                    File.SetAttributes(blockedPath, FileAttributes.Normal);
                }
            }
        }

        [Fact]
        public void Reload_ReportsUnreadableDefinitionInsteadOfThrowing()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);

                // 閉じの --- が無いフロントマターは読み込みに失敗する。
                WriteDefinition(directory, GptLightPath, "---\ncodex_model: broken\n本文\n");
                settings.Reload();

                Assert.Single(settings.UnreadableFiles);
                Assert.Contains(GptLightPath, settings.UnreadableFiles[0]);
                Assert.False(settings.CanSave);
                Assert.Empty(settings.MissingFiles);
            }
        }

        [Fact]
        public void Validate_RejectsValuesWithDisallowedCharacters()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);

                settings.ImplHard.ClaudeModel = "foo: bar";
                settings.ImplLight.CodexModel = "[";
                ConsoleSettingsSaveResult result = settings.Save();

                Assert.False(result.Succeeded);
                Assert.Contains(result.ValidationErrors, e => e.Contains(ClaudeHardPath) && e.Contains("model"));
                Assert.Contains(result.ValidationErrors, e => e.Contains(GptLightPath) && e.Contains("codex_model"));
                Assert.Contains("claude-hard-model", ReadDefinition(directory, ClaudeHardPath));
            }
        }

        [Fact]
        public void HasChanges_StaysTrueAfterInterruptedSave()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);
                string blockedPath = GetPath(directory, GptLightPath);
                File.SetAttributes(blockedPath, FileAttributes.ReadOnly);
                try
                {
                    settings.ImplHard.ClaudeModel = "claude-hard-model-updated";
                    settings.ImplLight.CodexModel = "codex-light-model-updated";
                    Assert.ThrowsAny<Exception>(() => settings.Save());

                    // 入力を読み込み時の値へ戻しても、ディスクには保存済みの値が残る。
                    settings.ImplHard.ClaudeModel = "claude-hard-model";
                    settings.ImplLight.CodexModel = "codex-light-model";

                    Assert.True(settings.HasChanges);
                }
                finally
                {
                    File.SetAttributes(blockedPath, FileAttributes.Normal);
                }

                settings.Reload();
                Assert.False(settings.HasChanges);
            }
        }

        [Fact]
        public void Save_FailsValidationWhenDefinitionIsUnreadable()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);
                WriteDefinition(directory, ClaudeHardPath, "---\nmodel: broken\n本文\n");
                settings.Reload();

                settings.ImplStandard.ClaudeModel = "claude-standard-model-updated";
                ConsoleSettingsSaveResult result = settings.Save();

                Assert.False(result.Succeeded);
                Assert.Contains(result.ValidationErrors, e => e.Contains(ClaudeHardPath));
                Assert.Empty(result.ChangedFiles);
            }
        }

        [Fact]
        public void Load_TreatsInvalidCodexEnabledAsDisabledAndReportsIt()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                WriteDefinition(
                    directory,
                    GptLightPath,
                    GptDefinition("codex-light-model", "high", null).Replace(
                        "codex_sandbox: workspace-write\n",
                        "codex_sandbox: workspace-write\ncodex_enabled: typo\n"));
                var settings = CreateSettings(directory);

                Assert.False(settings.CodexEnabled);
                Assert.Single(settings.CodexEnabledInvalidFiles);
                Assert.Contains(GptLightPath, settings.CodexEnabledInvalidFiles[0]);
            }
        }

        [Fact]
        public void Save_RepairsInvalidCodexEnabled()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                WriteDefinition(
                    directory,
                    GptLightPath,
                    GptDefinition("codex-light-model", "high", null).Replace(
                        "codex_sandbox: workspace-write\n",
                        "codex_sandbox: workspace-write\ncodex_enabled: typo\n"));
                var settings = CreateSettings(directory);

                // 利用者が何も変えていなくても、修復のために保存できる必要がある。
                Assert.False(settings.HasChanges);
                Assert.True(settings.NeedsSave);

                Assert.True(settings.Save().Succeeded);

                Assert.Contains("codex_enabled: \"false\"", ReadDefinition(directory, GptLightPath));
                Assert.Empty(settings.CodexEnabledInvalidFiles);
                Assert.False(settings.NeedsSave);
            }
        }

        [Fact]
        public void Save_FailsWhenDefinitionIsDeletedAfterLoad()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);

                File.Delete(GetPath(directory, GptStandardPath));
                settings.ImplHard.ClaudeModel = "claude-hard-model-updated";
                ConsoleSettingsSaveResult result = settings.Save();

                Assert.False(result.Succeeded);
                Assert.Contains(result.ValidationErrors, e => e.Contains(GptStandardPath));
                Assert.Contains("claude-hard-model\n", ReadDefinition(directory, ClaudeHardPath));
            }
        }

        [Fact]
        public void Load_FillsOmittedGptEffortWithScriptDefault()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                WriteDefinition(
                    directory,
                    GptLightPath,
                    "---\ncodex_home: ~/.codex\ncodex_model: codex-light-model\ncodex_sandbox: workspace-write\n---\n本文\n");
                var settings = CreateSettings(directory);

                Assert.Equal("medium", settings.ImplLight.CodexReasoningEffort);
                Assert.Empty(settings.Validate());
            }
        }

        [Fact]
        public void Save_DoesNotAddOmittedGptEffortWhenUnchanged()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                WriteDefinition(
                    directory,
                    GptLightPath,
                    "---\ncodex_home: ~/.codex\ncodex_model: codex-light-model\ncodex_sandbox: workspace-write\n---\n本文\n");
                var settings = CreateSettings(directory);

                settings.ImplHard.ClaudeModel = "claude-hard-model-updated";
                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Equal(new[] { ClaudeHardPath }, result.ChangedFiles);
                Assert.DoesNotContain("codex_reasoning_effort", ReadDefinition(directory, GptLightPath));
            }
        }

        [Fact]
        public void Save_AddsGptEffortWhenChangedFromDefault()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                WriteDefinition(
                    directory,
                    GptLightPath,
                    "---\ncodex_home: ~/.codex\ncodex_model: codex-light-model\ncodex_sandbox: workspace-write\n---\n本文\n");
                var settings = CreateSettings(directory);

                settings.ImplLight.CodexReasoningEffort = "xhigh";
                Assert.True(settings.Save().Succeeded);

                Assert.Contains("codex_reasoning_effort: \"xhigh\"", ReadDefinition(directory, GptLightPath));
            }
        }

        [Fact]
        public void Save_RepairsOnlyTheDefinitionWithInvalidCodexEnabled()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                WriteDefinition(
                    directory,
                    GptStandardPath,
                    GptDefinition("codex-standard-model", "xhigh", true));
                WriteDefinition(
                    directory,
                    GptLightPath,
                    GptDefinition("codex-light-model", "high", null).Replace(
                        "codex_sandbox: workspace-write\n",
                        "codex_sandbox: workspace-write\ncodex_enabled: typo\n"));
                var settings = CreateSettings(directory);

                settings.ImplHard.ClaudeModel = "claude-hard-model-updated";
                Assert.True(settings.Save().Succeeded);

                // 不正値を持つ light だけを直し、正常な standard は触らない。
                Assert.Contains("codex_enabled: \"false\"", ReadDefinition(directory, GptLightPath));
                Assert.Contains("codex_enabled: true\n", ReadDefinition(directory, GptStandardPath));
            }
        }

        [Theory]
        [InlineData("/d/accounts/codex", "D:\\accounts\\codex")]
        [InlineData("/c/Users/test/.codex", "C:\\Users\\test\\.codex")]
        [InlineData("/d", "D:\\")]
        public void Load_ConvertsMsysStyleCodexHomeLikeTheScript(string codexHome, string expected)
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                WriteDefinition(
                    directory,
                    GptLightPath,
                    "---\ncodex_home: " + codexHome
                        + "\ncodex_model: codex-light-model\ncodex_reasoning_effort: high\ncodex_sandbox: workspace-write\n---\n本文\n");

                var settings = CreateSettings(directory);

                Assert.Equal(expected, settings.ExpandedCodexHome);
            }
        }

        [Theory]
        [InlineData("null")]
        [InlineData("true")]
        [InlineData("False")]
        [InlineData("123")]
        [InlineData("1.5")]
        [InlineData("0x10")]
        [InlineData("-")]
        [InlineData("--")]
        [InlineData("-model")]
        [InlineData(".")]
        public void Validate_AcceptsValuesYamlWouldNotReadAsText(string model)
        {
            // 値は引用符で囲んで書くため、YAML の予約語や数値でも文字列として読まれる。
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);

                settings.ImplHard.ClaudeModel = model;

                Assert.Empty(settings.Validate());
            }
        }

        [Fact]
        public void Save_WritesClaudeModelInDoubleQuotes()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);

                settings.ImplHard.ClaudeModel = "true";
                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Contains("model: \"true\"", ReadDefinition(directory, ClaudeHardPath));

                settings.Reload();
                Assert.Equal("true", settings.ImplHard.ClaudeModel);
            }
        }

        [Fact]
        public void Load_ReadsQuotedCodexEnabledAsFalse()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                WriteDefinition(
                    directory,
                    GptLightPath,
                    GptDefinition("codex-light-model", "high", null).Replace(
                        "codex_sandbox: workspace-write\n",
                        "codex_sandbox: workspace-write\ncodex_enabled: \"false\"\n"));
                var settings = CreateSettings(directory);

                Assert.False(settings.CodexEnabled);
                Assert.Empty(settings.CodexEnabledInvalidFiles);
            }
        }

        [Theory]
        [InlineData("claude-opus-5")]
        [InlineData("gpt-5.6-luna")]
        [InlineData("claude-haiku-4-5-20251001")]
        public void Validate_AcceptsRealModelNames(string model)
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);

                settings.ImplHard.ClaudeModel = model;

                Assert.Empty(settings.Validate());
            }
        }

        [Fact]
        public void Reload_ReportsDefinitionThatIsNotValidUtf8()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = CreateSettings(directory);

                // Shift_JIS で保存された日本語は UTF-8 として復号できない。
                byte[] shiftJis = System.Text.Encoding.GetEncoding(932)
                    .GetBytes("---\nmodel: x\neffort: high\n---\n日本語の本文\n");
                File.WriteAllBytes(GetPath(directory, ClaudeHardPath), shiftJis);
                settings.Reload();

                Assert.Single(settings.UnreadableFiles);
                Assert.Contains(ClaudeHardPath, settings.UnreadableFiles[0]);
                Assert.False(settings.CanSave);
            }
        }

        [Fact]
        public void Validate_AcceptsUltraGptEffort()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);

                // codex debug models が ultra を返すモデルがあるため、保存で弾いてはならない。
                WriteDefinition(
                    directory,
                    GptLightPath,
                    GptDefinition("codex-light-model", "ultra", null));
                var settings = CreateSettings(directory);

                Assert.Equal("ultra", settings.ImplLight.CodexReasoningEffort);
                Assert.Empty(settings.Validate());

                settings.ImplStandard.CodexReasoningEffort = "ultra";
                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Contains("codex_reasoning_effort: \"ultra\"", ReadDefinition(directory, GptStandardPath));
            }
        }

        [Fact]
        public void Load_ListsExistingCodexHomesInNameOrder()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);

                // .codex で始まらないもの、値として書けない名前のものは並べない。
                CreateCodexHomes(directory, ".codex-review", ".claude", ".codex 使えない名前");

                ConsoleSettings settings = CreateSettings(directory);

                Assert.Equal(
                    new[] { "~/.codex", "~/.codex-review", "~/.codex-subagent" },
                    settings.CodexHomeChoices);
                Assert.Equal("~/.codex", settings.CodexHome);
                Assert.True(settings.CodexHomeIsListed);
                Assert.False(settings.CodexHomeMismatch);
            }
        }

        [Fact]
        public void Save_WritesSelectedCodexHomeToBothDefinitionsAndKeepsInlineComment()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                WriteDefinition(directory, GptLightPath, WithCodexHomeComment(
                    GptDefinition("codex-light-model", "high", null)));
                byte[] hardBefore = File.ReadAllBytes(GetPath(directory, ClaudeHardPath));
                ConsoleSettings settings = CreateSettings(directory);

                settings.CodexHome = "~/.codex-subagent";
                Assert.Equal(
                    new[] { "codex_home: ~/.codex → ~/.codex-subagent" },
                    settings.DescribeChanges());

                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Contains(GptStandardPath, result.ChangedFiles);
                Assert.Contains(GptLightPath, result.ChangedFiles);
                Assert.Contains(
                    "codex_home: \"~/.codex-subagent\"  # 認証ホーム",
                    ReadDefinition(directory, GptLightPath));
                Assert.Contains(
                    "codex_home: \"~/.codex-subagent\"",
                    ReadDefinition(directory, GptStandardPath));

                // codex_home を持たない定義は書き換えない。
                Assert.DoesNotContain(ClaudeHardPath, result.ChangedFiles);
                Assert.Equal(hardBefore, File.ReadAllBytes(GetPath(directory, ClaudeHardPath)));
                Assert.False(settings.HasChanges);
            }
        }

        [Fact]
        public void Save_AlignsMismatchedCodexHomeWithTheSelectedValue()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                WriteDefinition(directory, GptStandardPath, WithCodexHome(
                    GptDefinition("codex-standard-model", "xhigh", null),
                    "~/.codex-subagent"));
                ConsoleSettings settings = CreateSettings(directory);

                // 表示する値は impl-light の側とする。
                Assert.Equal("~/.codex", settings.CodexHome);
                Assert.True(settings.CodexHomeMismatch);
                Assert.True(settings.NeedsCodexHomeAlignment);
                Assert.False(settings.HasChanges);
                Assert.True(settings.NeedsSave);

                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Equal(new[] { GptStandardPath }, result.ChangedFiles);
                Assert.Contains("codex_home: \"~/.codex\"", ReadDefinition(directory, GptStandardPath));
                Assert.False(settings.CodexHomeMismatch);
                Assert.False(settings.NeedsSave);
            }
        }

        [Fact]
        public void Save_KeepsCodexHomeThatIsNotListed()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                WriteDefinition(directory, GptStandardPath, WithCodexHome(
                    GptDefinition("codex-standard-model", "xhigh", null),
                    "$USERPROFILE/.codex-missing"));
                WriteDefinition(directory, GptLightPath, WithCodexHome(
                    GptDefinition("codex-light-model", "high", null),
                    "$USERPROFILE/.codex-missing"));
                ConsoleSettings settings = CreateSettings(directory);

                Assert.Equal("$USERPROFILE/.codex-missing", settings.CodexHome);
                Assert.False(settings.CodexHomeIsListed);
                Assert.False(settings.CodexHomeExists);
                Assert.False(settings.CodexHomeMismatch);
                Assert.False(settings.NeedsSave);

                settings.ImplHard.ClaudeModel = "claude-hard-model-updated";
                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Equal(new[] { ClaudeHardPath }, result.ChangedFiles);
                Assert.Contains(
                    "codex_home: $USERPROFILE/.codex-missing",
                    ReadDefinition(directory, GptLightPath));
            }
        }

        [Fact]
        public void Reload_DiscardsCodexHomeSelection()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                ConsoleSettings settings = CreateSettings(directory);

                settings.CodexHome = "~/.codex-subagent";
                Assert.True(settings.HasChanges);

                settings.Reload();

                Assert.Equal("~/.codex", settings.CodexHome);
                Assert.False(settings.HasChanges);
            }
        }

        [Fact]
        public void ReloadPreservingEdits_KeepsSelectedCodexHomeAndReportsExternalChange()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                CreateCodexHomes(directory, ".codex-review");
                ConsoleSettings settings = CreateSettings(directory);
                settings.CodexHome = "~/.codex-subagent";

                WriteDefinition(directory, GptLightPath, WithCodexHome(
                    GptDefinition("codex-light-model", "high", null),
                    "~/.codex-review"));

                IReadOnlyList<string> conflicts = settings.ReloadPreservingEdits();

                Assert.Equal(
                    new[]
                    {
                        GptLightPath
                            + " の codex_home: 外部で ~/.codex-review に変わったが、入力中の ~/.codex-subagent を優先する"
                    },
                    conflicts);
                Assert.Equal("~/.codex-subagent", settings.CodexHome);
                Assert.True(settings.CodexHomeIsListed);
                Assert.True(settings.HasChanges);
            }
        }

        [Fact]
        public void ReloadPreservingEdits_ReportsExternalCodexHomeChangeOnImplStandard()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                CreateCodexHomes(directory, ".codex-review");
                ConsoleSettings settings = CreateSettings(directory);
                settings.CodexHome = "~/.codex-subagent";

                // impl-light は変えず、impl-standard の codex_home だけを外部で変える。
                WriteDefinition(directory, GptStandardPath, WithCodexHome(
                    GptDefinition("codex-standard-model", "xhigh", null),
                    "~/.codex-review"));

                IReadOnlyList<string> conflicts = settings.ReloadPreservingEdits();

                Assert.Equal(
                    new[]
                    {
                        GptStandardPath
                            + " の codex_home: 外部で ~/.codex-review に変わったが、入力中の ~/.codex-subagent を優先する"
                    },
                    conflicts);
                Assert.Equal("~/.codex-subagent", settings.CodexHome);
                Assert.True(settings.CodexHomeMismatch);
            }
        }

        [Fact]
        public void Save_RejectsCodexHomeRemovedAfterListing()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                ConsoleSettings settings = CreateSettings(directory);
                settings.CodexHome = "~/.codex-subagent";
                Assert.True(settings.CodexHomeIsListed);

                Directory.Delete(Path.Combine(HomePath(directory), ".codex-subagent"));

                ConsoleSettingsSaveResult result = settings.Save();

                Assert.False(result.Succeeded);
                Assert.Contains("認証ホームが存在しない: ~/.codex-subagent", result.ValidationErrors);
                Assert.Contains("codex_home: ~/.codex\n", ReadDefinition(directory, GptLightPath));
                Assert.Contains("codex_home: ~/.codex\n", ReadDefinition(directory, GptStandardPath));
            }
        }

        private static string WithCodexHome(string definition, string codexHome)
        {
            return definition.Replace("codex_home: ~/.codex\n", "codex_home: " + codexHome + "\n");
        }

        private static string WithCodexHomeComment(string definition)
        {
            return definition.Replace(
                "codex_home: ~/.codex\n",
                "codex_home: ~/.codex  # 認証ホーム\n");
        }

        private static void WriteMismatchedDefinitions(TemporaryDirectory directory)
        {
            WriteDefinitions(directory);
            WriteDefinition(
                directory,
                GptStandardPath,
                GptDefinition("codex-standard-model", "xhigh", false));
            WriteDefinition(
                directory,
                GptLightPath,
                GptDefinition("codex-light-model", "high", true));
        }

        // ~ の展開先と認証ホームの一覧を一時フォルダに閉じる。実在の %USERPROFILE% に依存させないためである。
        private static ConsoleSettings CreateSettings(TemporaryDirectory directory)
        {
            return new ConsoleSettings(directory.Path, HomePath(directory));
        }

        private static string HomePath(TemporaryDirectory directory)
        {
            return Path.Combine(directory.Path, "home");
        }

        private static void CreateCodexHomes(TemporaryDirectory directory, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                Directory.CreateDirectory(Path.Combine(HomePath(directory), names[i]));
            }
        }

        private static void WriteDefinitions(TemporaryDirectory directory, bool? codexEnabled = null)
        {
            CreateCodexHomes(directory, ".codex", ".codex-subagent");
            WriteDefinition(
                directory,
                ClaudeHardPath,
                ClaudeDefinition("impl-hard", "claude-hard-model", "high"));
            WriteDefinition(
                directory,
                ClaudeStandardPath,
                ClaudeDefinition("impl-standard", "claude-standard-model", "medium"));
            WriteDefinition(
                directory,
                ClaudeLightPath,
                ClaudeDefinition("impl-light", "claude-light-model", "low"));
            WriteDefinition(
                directory,
                GptStandardPath,
                GptDefinition("codex-standard-model", "xhigh", codexEnabled));
            WriteDefinition(
                directory,
                GptLightPath,
                GptDefinition("codex-light-model", "high", codexEnabled));
        }

        private static string ClaudeDefinition(string name, string model, string effort)
        {
            return "---\n"
                + "name: " + name + "\n"
                + "description: 日本語の実装担当定義\n"
                + "model: " + model + "\n"
                + "effort: " + effort + "\n"
                + "---\n"
                + "本文を1行置く。\n";
        }

        [Fact]
        public void Save_DoesNotAddEmptyCodexModelWhenDefinitionLacksItAndGptIsDisabled()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);

                // codex_model が無い定義は codex_enabled: false のときだけスクリプトを通る。
                // 保存で空の codex_model を足すと、変えていないファイルを書き換えることになる。
                string withoutModel = GptDefinition("codex-light-model", "high", false)
                    .Replace("codex_model: codex-light-model\n", string.Empty);
                WriteDefinition(directory, GptLightPath, withoutModel);
                var settings = CreateSettings(directory);

                Assert.Null(settings.ImplLight.CodexModel);
                Assert.False(settings.CodexEnabled);

                settings.ImplLight.CodexModel = string.Empty;
                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Empty(result.ChangedFiles);
                Assert.DoesNotContain("codex_model", ReadDefinition(directory, GptLightPath));
            }
        }

        private static string GptDefinition(string model, string reasoningEffort, bool? codexEnabled)
        {
            string enabled = codexEnabled.HasValue
                ? "codex_enabled: " + (codexEnabled.Value ? "true" : "false") + "\n"
                : string.Empty;
            return "---\n"
                + "codex_home: ~/.codex\n"
                + "codex_model: " + model + "\n"
                + "codex_reasoning_effort: " + reasoningEffort + "\n"
                + "codex_sandbox: workspace-write\n"
                + enabled
                + "---\n"
                + "GPT 経路の本文を1行置く。\n";
        }

        private static void WriteDefinition(
            TemporaryDirectory directory,
            string relativePath,
            string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GetPath(directory, relativePath)));
            directory.WriteFile(relativePath, content);
        }

        private static string ReadDefinition(TemporaryDirectory directory, string relativePath)
        {
            return directory.ReadText(GetPath(directory, relativePath));
        }

        private static string GetPath(TemporaryDirectory directory, string relativePath)
        {
            return Path.Combine(directory.Path, relativePath);
        }
    }
}
