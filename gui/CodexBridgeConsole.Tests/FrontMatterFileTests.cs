using System;
using System.IO;
using CodexBridgeConsole;
using Xunit;

namespace CodexBridgeConsole.Tests
{
    public sealed class FrontMatterFileTests
    {
        [Fact]
        public void SetValue_PreservesInlineComment()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.WriteFile(
                    "agent.md",
                    "---\n" +
                    "codex_home: ~/.codex  # 説明\n" +
                    "---\n" +
                    "本文\n");

                FrontMatterFile file = FrontMatterFile.Load(path);
                Assert.Equal("~/.codex", file.GetValue("codex_home"));

                file.SetValue("codex_home", "~/.codex-subagent");
                Assert.True(file.Save());

                Assert.Equal(
                    "---\n" +
                    "codex_home: ~/.codex-subagent  # 説明\n" +
                    "---\n" +
                    "本文\n",
                    directory.ReadText(path));
            }
        }

        [Fact]
        public void GetValue_DoesNotTreatInternalHashAsComment()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.WriteFile(
                    "agent.md",
                    "---\n" +
                    "value: before#inside  # 行内コメント\n" +
                    "---\n");

                FrontMatterFile file = FrontMatterFile.Load(path);

                Assert.Equal("before#inside", file.GetValue("value"));
            }
        }

        [Fact]
        public void GetValue_RemovesBothQuoteStyles()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.WriteFile(
                    "agent.md",
                    "---\n" +
                    "double: \"double value\"\n" +
                    "single: 'single value'\n" +
                    "---\n");

                FrontMatterFile file = FrontMatterFile.Load(path);

                Assert.Equal("double value", file.GetValue("double"));
                Assert.Equal("single value", file.GetValue("single"));
            }
        }

        [Fact]
        public void TryGetValue_DistinguishesMissingKeyFromEmptyValue()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.WriteFile(
                    "agent.md",
                    "---\n" +
                    "present:\n" +
                    "---\n");

                FrontMatterFile file = FrontMatterFile.Load(path);
                string value;

                Assert.True(file.TryGetValue("present", out value));
                Assert.Equal(string.Empty, value);
                Assert.False(file.TryGetValue("absent", out value));
                Assert.Equal(string.Empty, file.GetValue("present"));
                Assert.Null(file.GetValue("absent"));
            }
        }

        [Fact]
        public void GetValue_TreatsCommentOnlyLineAsEmptyValue()
        {
            // 行内コメントだけを空値として読むのは tools/codex-agent.sh の fm_get と異なる唯一の既知の相違である。
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.WriteFile(
                    "agent.md",
                    "---\n" +
                    "key:  # コメントだけ\n" +
                    "---\n");

                FrontMatterFile file = FrontMatterFile.Load(path);

                Assert.Equal(string.Empty, file.GetValue("key"));
            }
        }

        [Fact]
        public void GetValue_IgnoresKeyInBody()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.WriteFile(
                    "agent.md",
                    "---\n" +
                    "frontmatter_key: value\n" +
                    "---\n" +
                    "body_key: value\n");

                FrontMatterFile file = FrontMatterFile.Load(path);

                Assert.Null(file.GetValue("body_key"));
            }
        }

        [Fact]
        public void SetValue_AddsMissingKeyImmediatelyBeforeClosingDelimiter()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.WriteFile(
                    "agent.md",
                    "---\n" +
                    "existing: yes\n" +
                    "---\n" +
                    "body-key: no\n" +
                    "本文\n");

                FrontMatterFile file = FrontMatterFile.Load(path);
                file.SetValue("codex_enabled", "false");
                Assert.True(file.Save());

                Assert.Equal(
                    "---\n" +
                    "existing: yes\n" +
                    "codex_enabled: false\n" +
                    "---\n" +
                    "body-key: no\n" +
                    "本文\n",
                    directory.ReadText(path));
            }
        }

        [Theory]
        [InlineData("\r\n")]
        [InlineData("\n")]
        public void Save_PreservesLineEnding(string newline)
        {
            using (var directory = new TemporaryDirectory())
            {
                string input = "---" + newline
                    + "key: old" + newline
                    + "---" + newline
                    + "本文" + newline;
                string expected = "---" + newline
                    + "key: new" + newline
                    + "---" + newline
                    + "本文" + newline;
                string path = directory.WriteFile("agent.md", input);

                FrontMatterFile file = FrontMatterFile.Load(path);
                file.SetValue("key", "new");
                Assert.True(file.Save());

                Assert.Equal(expected, directory.ReadText(path));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Save_PreservesUtf8Bom(bool bom)
        {
            using (var directory = new TemporaryDirectory())
            {
                string input = "---\nkey: old\n---\n本文\n";
                string expected = "---\nkey: new\n---\n本文\n";
                string path = directory.WriteFile("agent.md", input, bom);

                FrontMatterFile file = FrontMatterFile.Load(path);
                file.SetValue("key", "new");
                Assert.True(file.Save());

                Assert.Equal(TemporaryDirectory.EncodeUtf8(expected, bom), File.ReadAllBytes(path));
            }
        }

        [Fact]
        public void DuplicateKeys_UseAndReplaceOnlyFirstLine()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.WriteFile(
                    "agent.md",
                    "---\n" +
                    "key: first\n" +
                    "key: second\n" +
                    "---\n");

                FrontMatterFile file = FrontMatterFile.Load(path);

                Assert.Equal("first", file.GetValue("key"));
                file.SetValue("key", "updated");
                Assert.True(file.Save());

                Assert.Equal(
                    "---\n" +
                    "key: updated\n" +
                    "key: second\n" +
                    "---\n",
                    directory.ReadText(path));
            }
        }

        [Fact]
        public void Save_PreservesBodyExactly()
        {
            using (var directory = new TemporaryDirectory())
            {
                const string body = "## 本文\r\n内容 # を保持\r\nkey: body value\r\n";
                string path = directory.WriteFile(
                    "agent.md",
                    "---\r\n" +
                    "key: old\r\n" +
                    "---\r\n" +
                    body);

                FrontMatterFile file = FrontMatterFile.Load(path);
                file.SetValue("key", "new");
                Assert.True(file.Save());

                Assert.EndsWith(body, directory.ReadText(path));
            }
        }

        [Fact]
        public void Load_ThrowsWhenFirstLineIsNotDelimiter()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.WriteFile("agent.md", "title\n---\n本文\n");

                Assert.Throws<InvalidDataException>(() => FrontMatterFile.Load(path));
            }
        }

        [Fact]
        public void Load_ThrowsWhenClosingDelimiterIsMissing()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.WriteFile("agent.md", "---\nkey: value\n本文\n");

                Assert.Throws<InvalidDataException>(() => FrontMatterFile.Load(path));
            }
        }

        [Fact]
        public void Save_DoesNotWriteWhenValueIsUnchanged()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.WriteFile("agent.md", "---\nkey: same\n---\n本文\n");
                byte[] original = File.ReadAllBytes(path);

                FrontMatterFile file = FrontMatterFile.Load(path);
                file.SetValue("key", "same");

                Assert.False(file.HasChanges);
                Assert.False(file.Save());
                Assert.Equal(original, File.ReadAllBytes(path));
            }
        }

        [Fact]
        public void RealDefinitionShape_AddsCodexEnabledBeforeBody()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.WriteFile(
                    "impl-light.md",
                    "---\n" +
                    "codex_home: ~/.codex  # 1 アカウント運用の既定値。2 アカウント運用では ~/.codex-subagent に変える\n" +
                    "codex_model: gpt-5.6-luna\n" +
                    "codex_reasoning_effort: xhigh\n" +
                    "codex_sandbox: workspace-write\n" +
                    "---\n" +
                    "あなたは実装担当である。\n");

                FrontMatterFile file = FrontMatterFile.Load(path);
                file.SetValue("codex_enabled", "false");
                Assert.True(file.Save());

                Assert.Equal(
                    "---\n" +
                    "codex_home: ~/.codex  # 1 アカウント運用の既定値。2 アカウント運用では ~/.codex-subagent に変える\n" +
                    "codex_model: gpt-5.6-luna\n" +
                    "codex_reasoning_effort: xhigh\n" +
                    "codex_sandbox: workspace-write\n" +
                    "codex_enabled: false\n" +
                    "---\n" +
                    "あなたは実装担当である。\n",
                    directory.ReadText(path));
            }
        }

    }
}
