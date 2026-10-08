using System.Collections.Generic;
using CodexBridgeConsole;
using Xunit;

namespace CodexBridgeConsole.Tests
{
    public sealed class ChoicesTests
    {
        [Fact]
        public void Choices_LoadsEmbeddedDefaultsWhenExternalFileIsAbsent()
        {
            using (var directory = new TemporaryDirectory())
            {
                Choices choices = Choices.Load(directory.Path);

                Assert.Equal(
                    new[]
                    {
                        "claude-fable-5-1",
                        "claude-fable-5",
                        "claude-opus-5-5",
                        "claude-opus-5",
                        "claude-sonnet-5-5",
                        "claude-sonnet-5",
                        "claude-haiku-5-5",
                        "claude-opus-4-8",
                        "claude-opus-4-7",
                        "claude-opus-4-6",
                        "claude-sonnet-4-6",
                        "claude-haiku-4-5"
                    },
                    choices.ClaudeModels);
                Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max" }, choices.ClaudeEfforts);
                Assert.Equal(new[] { "gpt-6.1-sol", "gpt-6-astra", "gpt-6-sol", "gpt-6-luna", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.5" }, choices.GptModels);
                Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max", "ultra" }, choices.GptEfforts);
                var expectedGptEfforts = new Dictionary<string, string[]>
                {
                    { "gpt-6.1-sol", new[] { "low", "medium", "high", "xhigh", "max", "ultra" } },
                    { "gpt-6-astra", new[] { "low", "medium", "high", "xhigh", "max", "ultra" } },
                    { "gpt-6-sol", new[] { "low", "medium", "high", "xhigh", "max", "ultra" } },
                    { "gpt-6-luna", new[] { "low", "medium", "high", "xhigh", "max" } },
                    { "gpt-5.6-sol", new[] { "low", "medium", "high", "xhigh", "max", "ultra" } },
                    { "gpt-5.6-terra", new[] { "low", "medium", "high", "xhigh", "max", "ultra" } },
                    { "gpt-5.6-luna", new[] { "low", "medium", "high", "xhigh", "max" } },
                    { "gpt-5.5", new[] { "low", "medium", "high", "xhigh" } }
                };
                foreach (KeyValuePair<string, string[]> expected in expectedGptEfforts)
                {
                    Assert.Equal(expected.Value, choices.GptEffortsFor(expected.Key));
                }
            }
        }

        [Fact]
        public void Choices_ReplacesAllDefaultsWithValidExternalFile()
        {
            using (var directory = new TemporaryDirectory())
            {
                directory.WriteFile(
                    "choices.json",
                    "{\"claudeModels\":[\"custom-claude\"],\"claudeEfforts\":[\"custom-effort\"],\"gptModels\":[\"custom-gpt\"],\"gptEfforts\":[\"custom-gpt-effort\"]}");

                Choices choices = Choices.Load(directory.Path);

                Assert.Equal(new[] { "custom-claude" }, choices.ClaudeModels);
                Assert.Equal(new[] { "custom-effort" }, choices.ClaudeEfforts);
                Assert.Equal(new[] { "custom-gpt" }, choices.GptModels);
                Assert.Equal(new[] { "custom-gpt-effort" }, choices.GptEfforts);
                Assert.Equal(choices.GptEfforts, choices.GptEffortsFor("custom-gpt"));
            }
        }

        [Theory]
        [InlineData("{")]
        [InlineData("{\"claudeModels\":[\"only-one-key\"]}")]
        public void Choices_FallsBackToEmbeddedDefaultsWhenExternalFileIsInvalid(string json)
        {
            using (var directory = new TemporaryDirectory())
            {
                directory.WriteFile("choices.json", json);

                Choices choices = Choices.Load(directory.Path);

                Assert.Equal("claude-fable-5-1", choices.ClaudeModels[0]);
                Assert.Equal("low", choices.GptEfforts[0]);
            }
        }

        [Fact]
        public void ClaudeEffortsFor_ReturnsFiveLevelsForModelThatSupportsXhigh()
        {
            using (var directory = new TemporaryDirectory())
            {
                Choices choices = Choices.Load(directory.Path);

                Assert.Equal(
                    new[] { "low", "medium", "high", "xhigh", "max" },
                    choices.ClaudeEffortsFor("claude-opus-5-5"));
            }
        }

        [Fact]
        public void ClaudeEffortsFor_OmitsXhighForModelThatDoesNotSupportIt()
        {
            using (var directory = new TemporaryDirectory())
            {
                Choices choices = Choices.Load(directory.Path);

                Assert.Equal(
                    new[] { "low", "medium", "high", "max" },
                    choices.ClaudeEffortsFor("claude-opus-4-6"));
            }
        }

        [Theory]
        [InlineData("claude-haiku-4-5")]
        [InlineData("unknown-model")]
        [InlineData("")]
        public void ClaudeEffortsFor_FallsBackToFlatListForModelOutsideTheTable(string model)
        {
            using (var directory = new TemporaryDirectory())
            {
                Choices choices = Choices.Load(directory.Path);

                Assert.Equal(choices.ClaudeEfforts, choices.ClaudeEffortsFor(model));
            }
        }

        [Fact]
        public void ClaudeEffortsFor_FallsBackToFlatListWhenExternalFileHasNoModelEfforts()
        {
            using (var directory = new TemporaryDirectory())
            {
                directory.WriteFile(
                    "choices.json",
                    "{\"claudeModels\":[\"custom-claude\"],\"claudeEfforts\":[\"low\",\"high\"],\"gptModels\":[\"custom-gpt\"],\"gptEfforts\":[\"low\"]}");

                Choices choices = Choices.Load(directory.Path);

                Assert.Equal(new[] { "low", "high" }, choices.ClaudeEfforts);
                Assert.Equal(choices.ClaudeEfforts, choices.ClaudeEffortsFor("custom-claude"));
            }
        }

        [Fact]
        public void GptEffortsFor_ReadsModelEffortsFromExternalFile()
        {
            using (var directory = new TemporaryDirectory())
            {
                directory.WriteFile(
                    "choices.json",
                    "{\"claudeModels\":[\"custom-claude\"],\"claudeEfforts\":[\"low\"],\"gptModels\":[\"custom-gpt\"],\"gptEfforts\":[\"low\",\"medium\",\"high\"],\"gptModelEfforts\":[{\"model\":\"custom-gpt\",\"efforts\":[\"low\",\"high\"]}]}");

                Choices choices = Choices.Load(directory.Path);

                Assert.Equal(new[] { "low", "high" }, choices.GptEffortsFor("custom-gpt"));
            }
        }

        [Theory]
        [InlineData("unknown-model")]
        [InlineData("")]
        public void GptEffortsFor_FallsBackToFlatListForModelOutsideTheTable(string model)
        {
            using (var directory = new TemporaryDirectory())
            {
                Choices choices = Choices.Load(directory.Path);

                Assert.Equal(choices.GptEfforts, choices.GptEffortsFor(model));
            }
        }

        [Fact]
        public void GptEffortsFor_ProvidesEffortListForNearestSupportedSelection()
        {
            using (var directory = new TemporaryDirectory())
            {
                Choices choices = Choices.Load(directory.Path);
                IReadOnlyList<string> efforts = choices.GptEffortsFor("gpt-5.5");

                Assert.Equal("xhigh", Choices.NearestSupportedEffort(efforts, "max"));
            }
        }

        [Fact]
        public void GptEffortsFor_UsesCatalogEffortsWhenCatalogContainsModel()
        {
            using (var directory = new TemporaryDirectory())
            {
                Choices choices = Choices.Load(directory.Path);
                CodexModelCatalog catalog = CodexModelCatalog.Parse(
                    "{\"models\":[{\"slug\":\"gpt-5.5\",\"visibility\":\"list\",\"supported_reasoning_levels\":[{\"effort\":\"low\"},{\"effort\":\"medium\"}]}]}");

                Assert.Equal(
                    new[] { "low", "medium" },
                    choices.GptEffortsFor(catalog, "gpt-5.5"));
            }
        }

        [Fact]
        public void GptEffortsFor_UsesModelDefaultsWhenCatalogDoesNotContainModel()
        {
            using (var directory = new TemporaryDirectory())
            {
                Choices choices = Choices.Load(directory.Path);
                CodexModelCatalog catalog = CodexModelCatalog.Parse(
                    "{\"models\":[{\"slug\":\"gpt-6-astra\",\"visibility\":\"list\",\"supported_reasoning_levels\":[{\"effort\":\"high\"}]}]}");

                Assert.Equal(
                    new[] { "low", "medium", "high", "xhigh" },
                    choices.GptEffortsFor(catalog, "gpt-5.5"));
            }
        }

        [Fact]
        public void GptEffortsFor_UsesFlatDefaultsWhenCatalogAndModelTableDoNotContainModel()
        {
            using (var directory = new TemporaryDirectory())
            {
                Choices choices = Choices.Load(directory.Path);
                CodexModelCatalog catalog = CodexModelCatalog.Parse(
                    "{\"models\":[{\"slug\":\"gpt-6-astra\",\"visibility\":\"list\",\"supported_reasoning_levels\":[{\"effort\":\"high\"}]}]}");

                Assert.Equal(
                    choices.GptEfforts,
                    choices.GptEffortsFor(catalog, "custom-gpt"));
            }
        }

        [Fact]
        public void GptEffortsFor_UsesModelDefaultsWhenCatalogIsMissing()
        {
            using (var directory = new TemporaryDirectory())
            {
                Choices choices = Choices.Load(directory.Path);

                Assert.Equal(
                    new[] { "low", "medium", "high", "xhigh" },
                    choices.GptEffortsFor(null, "gpt-5.5"));
            }
        }

        [Theory]
        [InlineData("medium", "medium")]
        [InlineData("max", "high")]
        public void GptEffortAfterModelChange_KeepsSupportedValueOrUsesCatalogDefaultForModelInCatalog(
            string current,
            string expected)
        {
            using (var directory = new TemporaryDirectory())
            {
                Choices choices = Choices.Load(directory.Path);
                CodexModelCatalog catalog = CodexModelCatalog.Parse(
                    "{\"models\":[{\"slug\":\"gpt-5.5\",\"visibility\":\"list\",\"default_reasoning_level\":\"high\",\"supported_reasoning_levels\":[{\"effort\":\"medium\"},{\"effort\":\"high\"}]}]}");

                Assert.Equal(expected, choices.GptEffortAfterModelChange(catalog, "gpt-5.5", current));
            }
        }

        [Fact]
        public void GptEffortAfterModelChange_UsesFirstCatalogEffortWhenCatalogHasNoDefault()
        {
            using (var directory = new TemporaryDirectory())
            {
                Choices choices = Choices.Load(directory.Path);
                CodexModelCatalog catalog = CodexModelCatalog.Parse(
                    "{\"models\":[{\"slug\":\"gpt-5.5\",\"visibility\":\"list\",\"supported_reasoning_levels\":[{\"effort\":\"low\"},{\"effort\":\"medium\"}]}]}");

                Assert.Equal("low", choices.GptEffortAfterModelChange(catalog, "gpt-5.5", "max"));
            }
        }

        [Fact]
        public void GptEffortAfterModelChange_UsesModelTableForModelMissingFromCatalog()
        {
            using (var directory = new TemporaryDirectory())
            {
                Choices choices = Choices.Load(directory.Path);
                CodexModelCatalog catalog = CodexModelCatalog.Parse(
                    "{\"models\":[{\"slug\":\"gpt-6-astra\",\"visibility\":\"list\",\"supported_reasoning_levels\":[{\"effort\":\"ultra\"}]}]}");

                Assert.Equal("xhigh", choices.GptEffortAfterModelChange(catalog, "gpt-5.5", "ultra"));
            }
        }

        [Fact]
        public void GptEffortAfterModelChange_KeepsValueForModelMissingFromCatalogAndModelTable()
        {
            using (var directory = new TemporaryDirectory())
            {
                Choices choices = Choices.Load(directory.Path);
                CodexModelCatalog catalog = CodexModelCatalog.Parse(
                    "{\"models\":[{\"slug\":\"gpt-6-astra\",\"visibility\":\"list\",\"supported_reasoning_levels\":[{\"effort\":\"high\"}]}]}");

                Assert.Equal("ultra", choices.GptEffortAfterModelChange(catalog, "custom-gpt", "ultra"));
            }
        }

        [Theory]
        [InlineData("high", new[] { "low", "medium", "high", "max" }, "high")]
        [InlineData("xhigh", new[] { "low", "medium", "high", "max" }, "high")]
        [InlineData("medium", new[] { "low" }, "low")]
        [InlineData("max", new[] { "low", "medium" }, "medium")]
        [InlineData("low", new[] { "medium", "high" }, "medium")]
        [InlineData("unknown-effort", new[] { "low", "medium" }, "unknown-effort")]
        public void NearestSupportedEffort_FallsBackToTheHighestSupportedValueAtOrBelowTheCurrentOne(
            string current,
            string[] efforts,
            string expected)
        {
            Assert.Equal(expected, Choices.NearestSupportedEffort(efforts, current));
        }

        [Fact]
        public void NearestSupportedEffort_KeepsCurrentValueWhenListIsEmpty()
        {
            Assert.Equal("high", Choices.NearestSupportedEffort(new string[0], "high"));
        }
    }
}
