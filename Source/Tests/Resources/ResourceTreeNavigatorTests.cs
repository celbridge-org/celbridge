using Celbridge.Resources;
using Celbridge.Resources.Helpers;
using Celbridge.Resources.Models;
using Celbridge.UserInterface;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Direct tests for the static tree-walking helpers. Builds a synthetic
/// IFolderResource tree (no filesystem, no registry) and exercises BuildKey
/// and FindResource on it.
/// </summary>
[TestFixture]
public class ResourceTreeNavigatorTests
{
    [Test]
    public void BuildKey_RootResource_ReturnsEmptyKey()
    {
        var root = new FolderResource(string.Empty, null);

        ResourceTreeNavigator.BuildKey(root).IsEmpty.Should().BeTrue();
    }

    [Test]
    public void BuildKey_TopLevelFile_ReturnsSingleSegment()
    {
        var root = new FolderResource(string.Empty, null);
        var file = new FileResource("a.txt", root, FakeIcon);
        root.AddChild(file);

        ResourceTreeNavigator.BuildKey(file).Path.Should().Be("a.txt");
    }

    [Test]
    public void BuildKey_NestedFile_JoinsSegmentsWithSlash()
    {
        var root = new FolderResource(string.Empty, null);
        var sub = new FolderResource("sub", root);
        root.AddChild(sub);
        var nested = new FileResource("note.md", sub, FakeIcon);
        sub.AddChild(nested);

        ResourceTreeNavigator.BuildKey(nested).Path.Should().Be("sub/note.md");
    }

    [Test]
    public void FindResource_EmptyKey_ReturnsRoot()
    {
        var root = new FolderResource(string.Empty, null);

        var result = ResourceTreeNavigator.FindResource(root, ResourceKey.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(root);
    }

    [Test]
    public void FindResource_FileSegment_ReturnsFileResource()
    {
        var root = new FolderResource(string.Empty, null);
        var file = new FileResource("a.txt", root, FakeIcon);
        root.AddChild(file);

        var result = ResourceTreeNavigator.FindResource(root, ResourceKey.Create("a.txt"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(file);
    }

    [Test]
    public void FindResource_NestedFile_TraversesAllSegments()
    {
        var root = new FolderResource(string.Empty, null);
        var sub = new FolderResource("sub", root);
        root.AddChild(sub);
        var nested = new FileResource("note.md", sub, FakeIcon);
        sub.AddChild(nested);

        var result = ResourceTreeNavigator.FindResource(root, ResourceKey.Create("sub/note.md"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(nested);
    }

    [Test]
    public void FindResource_FolderSegment_ReturnsFolderResource()
    {
        var root = new FolderResource(string.Empty, null);
        var sub = new FolderResource("sub", root);
        root.AddChild(sub);

        var result = ResourceTreeNavigator.FindResource(root, ResourceKey.Create("sub"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(sub);
    }

    [Test]
    public void FindResource_UnknownSegment_FailsWithKeyInMessage()
    {
        var root = new FolderResource(string.Empty, null);

        var result = ResourceTreeNavigator.FindResource(root, ResourceKey.Create("missing"));

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("missing");
    }

    [Test]
    public void FindResource_DefaultComparison_IsCaseSensitive()
    {
        var root = new FolderResource(string.Empty, null);
        var sub = new FolderResource("Docs", root);
        root.AddChild(sub);
        var leaf = new FileResource("Readme.md", sub, FakeIcon);
        sub.AddChild(leaf);

        ResourceTreeNavigator.FindResource(root, ResourceKey.Create("docs/readme.md")).IsFailure.Should().BeTrue();
    }

    [Test]
    public void FindResource_OrdinalIgnoreCase_MatchesMiscasedKey()
    {
        var root = new FolderResource(string.Empty, null);
        var sub = new FolderResource("Docs", root);
        root.AddChild(sub);
        var leaf = new FileResource("Readme.md", sub, FakeIcon);
        sub.AddChild(leaf);

        var result = ResourceTreeNavigator.FindResource(root, ResourceKey.Create("docs/readme.md"), ignoreCase: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(leaf);
        // BuildKey on the matched node recovers the canonical (disk-preserved) case.
        ResourceTreeNavigator.BuildKey(result.Value).Path.Should().Be("Docs/Readme.md");
    }

    [Test]
    public void BuildKey_FindResource_RoundTrip()
    {
        var root = new FolderResource(string.Empty, null);
        var sub = new FolderResource("docs", root);
        root.AddChild(sub);
        var leaf = new FileResource("readme.md", sub, FakeIcon);
        sub.AddChild(leaf);

        var key = ResourceTreeNavigator.BuildKey(leaf);
        var found = ResourceTreeNavigator.FindResource(root, key);

        found.IsSuccess.Should().BeTrue();
        found.Value.Should().BeSameAs(leaf);
    }

    private static readonly FileIconDefinition FakeIcon = new("x", "#000000", "fa-solid", "12");
}
