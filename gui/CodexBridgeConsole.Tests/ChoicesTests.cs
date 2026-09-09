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

                Assert.Equal(new[] { "claude-opus-5", "claude-sonnet-5", "claude-haiku-4-5-20251001" }, choices.ClaudeModels);
                Assert.Equal(new[] { "low", "medium", "high" }, choices.ClaudeEfforts);
                Assert.Equal(new[] { "gpt-5.6-luna", "gpt-5.6-sol" }, choices.GptModels);
                Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max" }, choices.GptEfforts);
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

                Assert.Equal("claude-opus-5", choices.ClaudeModels[0]);
                Assert.Equal("low", choices.GptEfforts[0]);
            }
        }
    }
}
