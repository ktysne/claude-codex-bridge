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
                        "claude-opus-5",
                        "claude-sonnet-5",
                        "claude-opus-4-8",
                        "claude-opus-4-7",
                        "claude-opus-4-6",
                        "claude-sonnet-4-6",
                        "claude-haiku-4-5"
                    },
                    choices.ClaudeModels);
                Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max" }, choices.ClaudeEfforts);
                Assert.Equal(new[] { "gpt-6-astra", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.5" }, choices.GptModels);
                Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max", "ultra" }, choices.GptEfforts);
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
                    choices.ClaudeEffortsFor("claude-opus-5"));
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
