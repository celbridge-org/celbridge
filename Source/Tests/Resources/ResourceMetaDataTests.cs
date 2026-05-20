using Celbridge.Explorer.Services;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.UserInterface.Services;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for the reference-graph subsystem of IResourceMetaData. The
/// frontmatter-index methods are not yet implemented and throw
/// NotImplementedException; those scenarios are covered separately.
/// </summary>
[TestFixture]
public class ResourceMetaDataTests
{
    private string _projectFolderPath = null!;
    private ResourceRegistry _resourceRegistry = null!;
    private ResourceMetaData _metaData = null!;
    private IMessengerService _messengerService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ResourceMetaDataTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        _resourceRegistry = new ResourceRegistry(
            Substitute.For<ILogger<ResourceRegistry>>(),
            _messengerService,
            fileIconService);
        _resourceRegistry.ProjectFolderPath = _projectFolderPath;

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspacePageLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _metaData = new ResourceMetaData(
            Substitute.For<ILogger<ResourceMetaData>>(),
            _messengerService,
            _workspaceWrapper,
            new TextBinarySniffer());
    }

    [TearDown]
    public void TearDown()
    {
        _metaData.Dispose();
        if (Directory.Exists(_projectFolderPath))
        {
            try
            {
                Directory.Delete(_projectFolderPath, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Test]
    public void ScanTextForReferences_FindsAllValidProjectReferences()
    {
        var text = "Some text with \"project:foo/bar.md\" and project:other/file.txt embedded.";

        var references = ResourceMetaData.ScanTextForReferences(text);

        references.Should().Contain(new ResourceKey("project:foo/bar.md"));
        references.Should().Contain(new ResourceKey("project:other/file.txt"));
    }

    [Test]
    public void ScanTextForReferences_SkipsInvalidCandidates()
    {
        // project: followed by an invalid character sequence (double slashes) should
        // not produce a reference.
        var text = "garbage project://invalid more garbage";

        var references = ResourceMetaData.ScanTextForReferences(text);

        references.Should().BeEmpty();
    }

    [Test]
    public void ScanTextForReferences_StopsAtKeyTerminators()
    {
        // The closing quote should terminate the candidate; bar.md is the full key.
        var text = "see \"project:foo/bar.md\" for details";

        var references = ResourceMetaData.ScanTextForReferences(text);

        references.Should().HaveCount(1);
        references.Should().Contain(new ResourceKey("project:foo/bar.md"));
    }

    [Test]
    public async Task RebuildAsync_ProducesReferenceGraph()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.md"),
            "This file references \"project:target.md\".");
        File.WriteAllText(Path.Combine(_projectFolderPath, "target.md"), "Target file.");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var rebuildResult = await _metaData.RebuildAsync();

        rebuildResult.IsSuccess.Should().BeTrue();
        rebuildResult.Value.ReferencesFound.Should().Be(1);

        var referencers = _metaData.GetReferencers(new ResourceKey("target.md"));
        referencers.Should().Contain(new ResourceKey("source.md"));

        var references = _metaData.GetReferences(new ResourceKey("source.md"));
        references.Should().Contain(new ResourceKey("target.md"));
    }

    [Test]
    public async Task RebuildAsync_SkipsBinaryFiles()
    {
        // A PNG containing the literal bytes "project:foo" should be skipped.
        // We synthesise a minimal PNG-ish binary file (8-byte signature + arbitrary bytes
        // including the "project:foo" string).
        var pngSignature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var marker = System.Text.Encoding.UTF8.GetBytes("project:foo");
        var pngBytes = new byte[pngSignature.Length + marker.Length];
        Buffer.BlockCopy(pngSignature, 0, pngBytes, 0, pngSignature.Length);
        Buffer.BlockCopy(marker, 0, pngBytes, pngSignature.Length, marker.Length);
        File.WriteAllBytes(Path.Combine(_projectFolderPath, "image.png"), pngBytes);

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        await _metaData.RebuildAsync();

        _metaData.GetReferencers(new ResourceKey("foo")).Should().BeEmpty();
    }

    [Test]
    public async Task RebuildAsync_SkipsOversizeFiles()
    {
        var oversize = new string('x', 11 * 1024 * 1024); // > 10MB
        File.WriteAllText(Path.Combine(_projectFolderPath, "big.txt"), oversize);

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var rebuildResult = await _metaData.RebuildAsync();

        rebuildResult.IsSuccess.Should().BeTrue();
        rebuildResult.Value.FilesSkipped.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task RebuildAsync_MarksServiceReady()
    {
        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        await _metaData.RebuildAsync();

        _metaData.IsReady.Should().BeTrue();
        await _metaData.WaitUntilReadyAsync();
    }

    [Test]
    public async Task GetAllReferencedTargets_ReturnsUnionOfTargets()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.md"),
            "Refers to \"project:x.md\" and \"project:y.md\".");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.md"),
            "Refers to \"project:y.md\".");
        File.WriteAllText(Path.Combine(_projectFolderPath, "x.md"), string.Empty);
        File.WriteAllText(Path.Combine(_projectFolderPath, "y.md"), string.Empty);

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();
        await _metaData.RebuildAsync();

        var targets = _metaData.GetAllReferencedTargets();
        targets.Should().Contain(new ResourceKey("x.md"));
        targets.Should().Contain(new ResourceKey("y.md"));
    }

    [Test]
    public void FrontmatterMethods_ThrowNotImplementedException()
    {
        // The frontmatter index methods are not yet implemented and throw.
        var resource = new ResourceKey("foo.md");

        Assert.Throws<NotImplementedException>(() => _metaData.GetFrontmatter(resource));
        Assert.Throws<NotImplementedException>(() => _metaData.FindByMetaData("tags", "x"));
        Assert.Throws<NotImplementedException>(() => _metaData.GetTags(resource));
        Assert.Throws<NotImplementedException>(() => _metaData.FindByTag("x"));
    }
}
