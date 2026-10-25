using Celbridge.Explorer.Helpers;
using Celbridge.Resources;

namespace Celbridge.Tests.Explorer;

/// <summary>
/// Covers what the Explorer tree hides unless the tree is showing hidden files: .cel files, and what the
/// Explorer's list excludes.
/// </summary>
[TestFixture]
public class ResourceTreeFilterTests
{
    private IResourcePolicy _policy = null!;

    [SetUp]
    public void Setup()
    {
        _policy = Substitute.For<IResourcePolicy>();
    }

    [Test]
    public void CelFile_IsHidden()
    {
        var sidecar = new ResourceKey("notes.md.cel");

        ResourceTreeFilter.IsHidden(sidecar, isFolder: false, _policy).Should().BeTrue();
    }

    [Test]
    public void FolderNamedLikeACelFile_IsNotHidden()
    {
        var folder = new ResourceKey("archive.cel");

        ResourceTreeFilter.IsHidden(folder, isFolder: true, _policy).Should().BeFalse();
    }

    [Test]
    public void ExplorerExcludeMatch_IsHidden()
    {
        var file = new ResourceKey(".gitignore");
        _policy.IsExplorerExcluded(file, false).Returns(true);

        ResourceTreeFilter.IsHidden(file, isFolder: false, _policy).Should().BeTrue();
    }

    [Test]
    public void SearchExcludeMatch_IsNotHidden()
    {
        var folder = new ResourceKey("node_modules");
        _policy.IsSearchExcluded(folder, true).Returns(true);

        ResourceTreeFilter.IsHidden(folder, isFolder: true, _policy).Should().BeFalse();
    }

    [Test]
    public void UnmatchedResource_IsNotHidden()
    {
        var file = new ResourceKey("main.py");

        ResourceTreeFilter.IsHidden(file, isFolder: false, _policy).Should().BeFalse();
    }
}
