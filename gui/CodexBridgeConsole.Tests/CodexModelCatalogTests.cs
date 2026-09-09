using CodexBridgeConsole;
using Xunit;

namespace CodexBridgeConsole.Tests
{
    public sealed class CodexModelCatalogTests
    {
        private const string SampleJson = @"{
  ""models"": [
    {
      ""slug"": ""gpt-6-astra"",
      ""visibility"": ""list"",
      ""default_reasoning_level"": ""high"",
      ""supported_reasoning_levels"": [ { ""effort"": ""medium"" }, { ""effort"": ""high"" }, { ""effort"": ""ultra"" } ]
    },
    {
      ""slug"": ""gpt-5.5"",
      ""visibility"": ""list"",
      ""default_reasoning_level"": ""medium"",
      ""supported_reasoning_levels"": [ { ""effort"": ""low"" }, { ""effort"": ""medium"" } ]
    },
    {
      ""slug"": ""gpt-internal"",
      ""visibility"": ""hide"",
      ""default_reasoning_level"": ""low"",
      ""supported_reasoning_levels"": [ { ""effort"": ""low"" } ]
    }
  ]
}";

        [Fact]
        public void Parse_KeepsListedModelsOnly()
        {
            CodexModelCatalog catalog = CodexModelCatalog.Parse(SampleJson);

            Assert.NotNull(catalog);
            Assert.Equal(new[] { "gpt-6-astra", "gpt-5.5" }, catalog.Models);
        }

        [Fact]
        public void Parse_ReturnsEffortsPerModel()
        {
            CodexModelCatalog catalog = CodexModelCatalog.Parse(SampleJson);

            Assert.Equal(new[] { "medium", "high", "ultra" }, catalog.EffortsFor("gpt-6-astra"));
            Assert.Equal(new[] { "low", "medium" }, catalog.EffortsFor("gpt-5.5"));
        }

        [Fact]
        public void Parse_ReturnsDefaultEffortPerModel()
        {
            CodexModelCatalog catalog = CodexModelCatalog.Parse(SampleJson);

            Assert.Equal("high", catalog.DefaultEffortFor("gpt-6-astra"));
            Assert.Equal("medium", catalog.DefaultEffortFor("gpt-5.5"));
        }

        [Fact]
        public void Parse_ReturnsNothingForUnknownModel()
        {
            CodexModelCatalog catalog = CodexModelCatalog.Parse(SampleJson);

            Assert.Empty(catalog.EffortsFor("gpt-unknown"));
            Assert.Null(catalog.DefaultEffortFor("gpt-unknown"));
        }

        [Fact]
        public void Parse_SkipsEntriesWithoutSlugOrReasoningLevels()
        {
            const string json = @"{
  ""models"": [
    { ""visibility"": ""list"", ""supported_reasoning_levels"": [ { ""effort"": ""low"" } ] },
    { ""slug"": ""gpt-no-levels"", ""visibility"": ""list"" },
    {
      ""slug"": ""gpt-5.6-luna"",
      ""visibility"": ""list"",
      ""default_reasoning_level"": ""medium"",
      ""supported_reasoning_levels"": [ { ""effort"": ""medium"" } ]
    }
  ]
}";

            CodexModelCatalog catalog = CodexModelCatalog.Parse(json);

            Assert.Equal(new[] { "gpt-5.6-luna" }, catalog.Models);
        }

        [Theory]
        [InlineData("{ \"models\": [")]
        [InlineData("これは JSON ではない")]
        [InlineData("")]
        public void Parse_ReturnsNullForBrokenInput(string json)
        {
            Assert.Null(CodexModelCatalog.Parse(json));
        }

        [Fact]
        public void Parse_IgnoresUnusedMembers()
        {
            const string json = @"{
  ""version"": 1,
  ""models"": [
    {
      ""slug"": ""gpt-5.6-sol"",
      ""visibility"": ""list"",
      ""default_reasoning_level"": ""xhigh"",
      ""base_instructions"": ""ここに長い内部指示文が入る"",
      ""supported_reasoning_levels"": [ { ""effort"": ""high"", ""display_name"": ""High"" } ]
    }
  ]
}";

            CodexModelCatalog catalog = CodexModelCatalog.Parse(json);

            Assert.Equal(new[] { "gpt-5.6-sol" }, catalog.Models);
            Assert.Equal(new[] { "high" }, catalog.EffortsFor("gpt-5.6-sol"));
            Assert.Equal("xhigh", catalog.DefaultEffortFor("gpt-5.6-sol"));
        }
    }
}
