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

                var settings = new ConsoleSettings(directory.Path);

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

                var settings = new ConsoleSettings(directory.Path);

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

                var settings = new ConsoleSettings(directory.Path);

                Assert.True(settings.CodexEnabled);
            }
        }

        [Fact]
        public void Save_DisablingCodexAddsFalseToBothCodexDefinitions()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = new ConsoleSettings(directory.Path)
                {
                    CodexEnabled = false
                };

                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Contains(GptStandardPath, result.ChangedFiles);
                Assert.Contains(GptLightPath, result.ChangedFiles);
                Assert.Contains("codex_enabled: false", ReadDefinition(directory, GptStandardPath));
                Assert.Contains("codex_enabled: false", ReadDefinition(directory, GptLightPath));
            }
        }

        [Fact]
        public void Save_WithoutChangesDoesNotRewriteMissingCodexEnabledKeys()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = new ConsoleSettings(directory.Path);
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
                var settings = new ConsoleSettings(directory.Path)
                {
                    CodexEnabled = true
                };

                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Contains(GptStandardPath, result.ChangedFiles);
                Assert.Contains(GptLightPath, result.ChangedFiles);
                Assert.Contains("codex_enabled: true", ReadDefinition(directory, GptStandardPath));
                Assert.Contains("codex_enabled: true", ReadDefinition(directory, GptLightPath));
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

                var settings = new ConsoleSettings(directory.Path);
                settings.ImplHard.ClaudeModel = "claude-hard-model-updated";
                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Single(result.ChangedFiles);
                Assert.Equal(ClaudeHardPath, result.ChangedFiles[0]);
                Assert.Contains(
                    "model: claude-hard-model-updated",
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
                var settings = new ConsoleSettings(directory.Path);
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
                var settings = new ConsoleSettings(directory.Path)
                {
                    CodexEnabled = false
                };
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
                var settings = new ConsoleSettings(directory.Path);
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
        public void Save_ReportsAlreadySavedFilesWhenInterrupted()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteDefinitions(directory);
                var settings = new ConsoleSettings(directory.Path);

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

                var settings = new ConsoleSettings(directory.Path);

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
                var settings = new ConsoleSettings(directory.Path);

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
                var settings = new ConsoleSettings(directory.Path);

                settings.CodexEnabled = true;
                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Contains(GptStandardPath, result.ChangedFiles);
                Assert.Contains("codex_enabled: true", ReadDefinition(directory, GptStandardPath));
                Assert.Contains("codex_enabled: true", ReadDefinition(directory, GptLightPath));
            }
        }

        [Fact]
        public void Save_NormalizesMismatchedCodexEnabledWhenToggleIsOperated()
        {
            using (var directory = new TemporaryDirectory())
            {
                WriteMismatchedDefinitions(directory);
                var settings = new ConsoleSettings(directory.Path);

                // 食い違いは無効として表示するため、両方を無効にする操作は表示上の値が変わらない。
                // 画面がトグルを操作したことを伝えると、表示どおりの値を両方へ書き戻す。
                Assert.False(settings.CodexEnabled);
                settings.CodexEnabledExplicit = true;
                ConsoleSettingsSaveResult result = settings.Save();

                Assert.True(result.Succeeded);
                Assert.Contains(GptLightPath, result.ChangedFiles);
                Assert.Contains("codex_enabled: false", ReadDefinition(directory, GptStandardPath));
                Assert.Contains("codex_enabled: false", ReadDefinition(directory, GptLightPath));
            }
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

        private static void WriteDefinitions(TemporaryDirectory directory, bool? codexEnabled = null)
        {
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
