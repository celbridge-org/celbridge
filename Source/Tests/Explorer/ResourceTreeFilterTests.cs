using Celbridge.Explorer.Helpers;
using Celbridge.Resources;

namespace Celbridge.Tests.Explorer;

/// <summary>
/// Covers what the Explorer tree draws: the sidecar kinds it never draws, the hide patterns the
/// Show Hidden Files toggle reverses, and the expander a folder only offers when a child survives both.
/// </summary>
[TestFixture]
public class ResourceTreeFilterTests
{
    private IResourceRegistry _registry = null!;
    private IResourcePolicy _policy = null!;

    [SetUp]
    public void Setup()
    {
        _registry = Substitute.For<IResourceRegistry>();
        _policy = Substitute.For<IResourcePolicy>();
    }

    [Test]
    public void SidecarKinds_AreNeverDrawn()
    {
        foreach (var kind in new[] { FileKind.Sidecar, FileKind.Orphan, FileKind.InvalidSidecar })
        {
            var file = CreateFile("notes.md.cel", kind);

            ResourceTreeFilter.IsDrawableKind(file).Should().BeFalse($"{kind} is project metadata");

            // Showing hidden files does not bring metadata back, because it was never hidden by a pattern.
            ResourceTreeFilter.IsDrawn(file, _registry, _policy, showHiddenFiles: true).Should().BeFalse();
        }
    }

    [Test]
    public void ContentFileAndFolder_AreDrawableKinds()
    {
        ResourceTreeFilter.IsDrawableKind(CreateFile("main.py", FileKind.PlainData)).Should().BeTrue();
        ResourceTreeFilter.IsDrawableKind(CreateFolder("src")).Should().BeTrue();
    }

    [Test]
    public void HiddenResource_IsDrawnOnlyWhileShowingHiddenFiles()
    {
        var file = CreateFile(".gitignore", FileKind.PlainData);
        StubHidden(file, isHidden: true);

        ResourceTreeFilter.IsDrawn(file, _registry, _policy, showHiddenFiles: false).Should().BeFalse();
        ResourceTreeFilter.IsDrawn(file, _registry, _policy, showHiddenFiles: true).Should().BeTrue();
    }

    [Test]
    public void VisibleResource_IsDrawnEitherWay()
    {
        var file = CreateFile("main.py", FileKind.PlainData);
        StubHidden(file, isHidden: false);

        ResourceTreeFilter.IsDrawn(file, _registry, _policy, showHiddenFiles: false).Should().BeTrue();
        ResourceTreeFilter.IsDrawn(file, _registry, _policy, showHiddenFiles: true).Should().BeTrue();
    }

    [Test]
    public void IsDrawn_AsksThePolicyForTheResourceKind()
    {
        // A folders-only hide pattern needs the kind to answer correctly, so the folder flag has to
        // reach the policy rather than being assumed.
        var folder = CreateFolder("dist");
        StubHidden(folder, isHidden: true);

        ResourceTreeFilter.IsDrawn(folder, _registry, _policy, showHiddenFiles: false).Should().BeFalse();

        _policy.Received().IsHidden(Arg.Any<ResourceKey>(), true);
    }

    [Test]
    public void FolderOfOnlyHiddenChildren_OffersNoExpander()
    {
        // Without this the chevron opens onto nothing, because the rows it promises are filtered out.
        var hiddenChild = CreateFile("build.log", FileKind.PlainData);
        StubHidden(hiddenChild, isHidden: true);

        var folder = CreateFolder("logs", hiddenChild);

        ResourceTreeFilter.HasDrawableChildren(folder, _registry, _policy, showHiddenFiles: false)
            .Should().BeFalse();

        ResourceTreeFilter.HasDrawableChildren(folder, _registry, _policy, showHiddenFiles: true)
            .Should().BeTrue("the rows exist again once hidden files are shown");
    }

    [Test]
    public void FolderOfOnlySidecars_OffersNoExpander()
    {
        var sidecar = CreateFile("notes.md.cel", FileKind.Sidecar);
        var folder = CreateFolder("data", sidecar);

        ResourceTreeFilter.HasDrawableChildren(folder, _registry, _policy, showHiddenFiles: false)
            .Should().BeFalse();
        ResourceTreeFilter.HasDrawableChildren(folder, _registry, _policy, showHiddenFiles: true)
            .Should().BeFalse();
    }

    [Test]
    public void FolderWithOneVisibleChild_OffersAnExpander()
    {
        var hiddenChild = CreateFile("build.log", FileKind.PlainData);
        StubHidden(hiddenChild, isHidden: true);

        var visibleChild = CreateFile("main.py", FileKind.PlainData);
        StubHidden(visibleChild, isHidden: false);

        var folder = CreateFolder("src", hiddenChild, visibleChild);

        ResourceTreeFilter.HasDrawableChildren(folder, _registry, _policy, showHiddenFiles: false)
            .Should().BeTrue();
    }

    [Test]
    public void EmptyFolder_OffersNoExpander()
    {
        var folder = CreateFolder("empty");

        ResourceTreeFilter.HasDrawableChildren(folder, _registry, _policy, showHiddenFiles: false)
            .Should().BeFalse();
    }

    private IFileResource CreateFile(string name, FileKind kind)
    {
        var file = Substitute.For<IFileResource>();
        file.Name.Returns(name);
        file.FileKind.Returns(kind);

        return file;
    }

    private IFolderResource CreateFolder(string name, params IResource[] children)
    {
        var folder = Substitute.For<IFolderResource>();
        folder.Name.Returns(name);
        folder.Children.Returns(children.ToList());

        return folder;
    }

    private void StubHidden(IResource resource, bool isHidden)
    {
        var key = new ResourceKey(resource.Name);

        _registry.GetResourceKey(resource).Returns(key);
        _policy.IsHidden(key, Arg.Any<bool>()).Returns(isHidden);
    }
}
