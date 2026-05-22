using Celbridge.Resources;
using Celbridge.Resources.Helpers;

namespace Celbridge.Tests.Resources;

[TestFixture]
public class SidecarHelperTests
{
    [Test]
    public void Parse_RoundTripsFrontmatterOnlyFile()
    {
        var text = "tags = [\"a\", \"b\"]\npriority = \"high\"\n";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeTrue();
        var parsed = result.Value;
        parsed.Frontmatter.Should().ContainKey("priority");
        parsed.Frontmatter["priority"].Should().Be("high");
        parsed.Blocks.Should().BeEmpty();
    }

    [Test]
    public void Parse_AcceptsEmptyFile()
    {
        var result = SidecarHelper.Parse(string.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Frontmatter.Should().BeEmpty();
        result.Value.Blocks.Should().BeEmpty();
    }

    [Test]
    public void Parse_AcceptsFileWithSingleNamedBlock()
    {
        var text = "tags = [\"meeting\"]\n+++ \"celbridge.notes.note-document.content\"\n# Meeting Notes\n\nBody text.";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeTrue();
        result.Value.Frontmatter.Should().ContainKey("tags");
        result.Value.Blocks.Should().HaveCount(1);
        result.Value.Blocks[0].Name.Should().Be("celbridge.notes.note-document.content");
        result.Value.Blocks[0].Content.Should().Contain("# Meeting Notes");
        result.Value.Blocks[0].Content.Should().Contain("Body text.");
    }

    [Test]
    public void Parse_PreservesOrderOfMultipleBlocks()
    {
        var text = "tags = [\"pixel-art\"]\n+++ \"celbridge.piskel.canvas\"\nfirst block content\n+++ \"celbridge.piskel.layers\"\nsecond block content";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeTrue();
        result.Value.Blocks.Should().HaveCount(2);
        result.Value.Blocks[0].Name.Should().Be("celbridge.piskel.canvas");
        result.Value.Blocks[1].Name.Should().Be("celbridge.piskel.layers");
        result.Value.Blocks[0].Content.Should().Contain("first block content");
        result.Value.Blocks[1].Content.Should().Contain("second block content");
    }

    [Test]
    public void Parse_AcceptsFileWithBlocksAndNoFrontmatter()
    {
        var text = "+++ \"only.block\"\njust the block, no frontmatter";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeTrue();
        result.Value.Frontmatter.Should().BeEmpty();
        result.Value.Blocks.Should().HaveCount(1);
        result.Value.Blocks[0].Content.Should().Contain("just the block");
    }

    [Test]
    public void Parse_RejectsDuplicateBlockNames()
    {
        var text = "+++ \"a\"\nfirst\n+++ \"a\"\nsecond";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeFalse();
        result.FirstErrorMessage.Should().Contain("duplicate block name");
    }

    [Test]
    public void Parse_RejectsMalformedFrontmatterToml()
    {
        var text = "not valid toml at all = !!!";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeFalse();
    }

    [Test]
    public void Parse_DoesNotTreatUppercaseFenceAsFence()
    {
        // A line "+++ \"Block\"" with uppercase B doesn't match the regex, so
        // it remains part of the frontmatter, which causes the TOML parse to
        // fail and the file classifies as broken.
        var text = "+++ \"Block\"\nbody";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeFalse();
    }

    [Test]
    public void Compose_RoundTripsFrontmatterOnly()
    {
        var frontmatter = new Dictionary<string, object>
        {
            ["title"] = "My Notes",
            ["tags"] = new List<object> { "meeting", "todo" },
        };

        var composed = SidecarHelper.Compose(frontmatter, Array.Empty<SidecarBlock>());
        var parseResult = SidecarHelper.Parse(composed);

        parseResult.IsSuccess.Should().BeTrue();
        parseResult.Value.Frontmatter["title"].Should().Be("My Notes");
        parseResult.Value.Blocks.Should().BeEmpty();
    }

    [Test]
    public void Compose_RoundTripsFrontmatterPlusBlocks()
    {
        var frontmatter = new Dictionary<string, object>
        {
            ["editor"] = "celbridge.notes.note-document",
            ["tags"] = new List<object> { "meeting" },
        };
        var blocks = new List<SidecarBlock>
        {
            new("celbridge.notes.note-document.content", "# Meeting Notes\n\nBody.\n"),
            new("celbridge.notes.note-document.revisions", "rev-1\nrev-2\n"),
        };

        var composed = SidecarHelper.Compose(frontmatter, blocks);
        var parseResult = SidecarHelper.Parse(composed);

        parseResult.IsSuccess.Should().BeTrue();
        parseResult.Value.Frontmatter["editor"].Should().Be("celbridge.notes.note-document");
        parseResult.Value.Blocks.Should().HaveCount(2);
        parseResult.Value.Blocks[0].Name.Should().Be("celbridge.notes.note-document.content");
        parseResult.Value.Blocks[0].Content.Should().Contain("# Meeting Notes");
        parseResult.Value.Blocks[1].Name.Should().Be("celbridge.notes.note-document.revisions");
        parseResult.Value.Blocks[1].Content.Should().Contain("rev-1");
    }

    [Test]
    public void Compose_HandlesEmptyFrontmatter()
    {
        var blocks = new List<SidecarBlock>
        {
            new("just.block", "hello body"),
        };

        var composed = SidecarHelper.Compose(new Dictionary<string, object>(), blocks);
        var parseResult = SidecarHelper.Parse(composed);

        parseResult.IsSuccess.Should().BeTrue();
        parseResult.Value.Frontmatter.Should().BeEmpty();
        parseResult.Value.Blocks.Should().HaveCount(1);
        parseResult.Value.Blocks[0].Content.Should().Contain("hello body");
    }

    [Test]
    public void Compose_RejectsInvalidBlockName()
    {
        var blocks = new List<SidecarBlock>
        {
            new("Bad Name", "content"),
        };

        var act = () => SidecarHelper.Compose(new Dictionary<string, object>(), blocks);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Parse_TreatsBlockContentWithoutTrailingNewlineAsEquivalent()
    {
        // Writing "three" and writing "three\n" both store as the same on-disk
        // bytes and parse back to the same SidecarBlock.Content value. The
        // trailing newline is a between-blocks separator, not part of content.
        var blocksA = new List<SidecarBlock>
        {
            new("test.alpha", "three"),
        };
        var blocksB = new List<SidecarBlock>
        {
            new("test.alpha", "three\n"),
        };

        var composedA = SidecarHelper.Compose(new Dictionary<string, object>(), blocksA);
        var composedB = SidecarHelper.Compose(new Dictionary<string, object>(), blocksB);

        composedA.Should().Be(composedB);

        var parsedA = SidecarHelper.Parse(composedA).Value;
        var parsedB = SidecarHelper.Parse(composedB).Value;

        parsedA.Blocks[0].Content.Should().Be("three");
        parsedB.Blocks[0].Content.Should().Be("three");
    }

    [Test]
    public void Parse_BlockContentSizeIsStableAcrossAdjacentAppends()
    {
        // After appending a second block, the first block's byte count must
        // not change. Previously the first block "gained" a trailing newline
        // when it stopped being the last block, shifting reported sizes.
        var singleBlock = new List<SidecarBlock>
        {
            new("test.alpha", "three"),
        };
        var twoBlocks = new List<SidecarBlock>
        {
            new("test.alpha", "three"),
            new("test.beta", "four"),
        };

        var composedSingle = SidecarHelper.Compose(new Dictionary<string, object>(), singleBlock);
        var composedTwo = SidecarHelper.Compose(new Dictionary<string, object>(), twoBlocks);

        var parsedSingle = SidecarHelper.Parse(composedSingle).Value;
        var parsedTwo = SidecarHelper.Parse(composedTwo).Value;

        parsedSingle.Blocks[0].Content.Should().Be("three");
        parsedTwo.Blocks[0].Content.Should().Be("three");
        parsedTwo.Blocks[0].Content.Length.Should().Be(parsedSingle.Blocks[0].Content.Length);
    }

    [Test]
    public void Compose_NormalisesTomlFrontmatterToLfLineEndings()
    {
        // Tomlyn emits Environment.NewLine (CRLF on Windows) for the
        // frontmatter section. Compose normalises to LF so the whole sidecar
        // file uses a single line ending convention, matching the LF literals
        // used for fence lines and content terminators.
        var frontmatter = new Dictionary<string, object>
        {
            ["editor"] = "celbridge.test",
            ["tags"] = new List<object> { "alpha", "beta" },
        };
        var blocks = new List<SidecarBlock>
        {
            new("test.block", "body"),
        };

        var composed = SidecarHelper.Compose(frontmatter, blocks);

        composed.Should().NotContain("\r\n");
        composed.Should().NotContain("\r");
        composed.Should().Contain("\n");
    }

    [Test]
    public void Parse_CRLFBlockContentTerminatorIsStripped()
    {
        // A CRLF-terminated block content line yields the same logical content
        // value as an LF-terminated one. Round-trip preserves the line bytes
        // but does not leak the separator into Content.
        var text = "+++ \"test.alpha\"\r\nline-one\r\n+++ \"test.beta\"\r\nline-two\r\n";

        var result = SidecarHelper.Parse(text);

        result.IsSuccess.Should().BeTrue();
        result.Value.Blocks[0].Content.Should().Be("line-one");
        result.Value.Blocks[1].Content.Should().Be("line-two");
    }
}
