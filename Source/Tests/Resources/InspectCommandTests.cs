using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for InspectCommand — the engine behind the data_inspect MCP tool.
/// The command resolves a per-resource scope, classifies each entry against
/// the workspace registry's sidecar report, and rolls per-status aggregate
/// counts into the response.
/// </summary>
[TestFixture]
public class InspectCommandTests
{
    private string _projectFolderPath = null!;
    private ResourceRegistry _resourceRegistry = null!;
    private RootHandlerRegistry _rootHandlerRegistry = null!;
    private IMessengerService _messengerService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private InspectCommand _command = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(InspectCommandTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _messengerService = new MessengerService();
        var iconService = new IconService();
        _rootHandlerRegistry = new RootHandlerRegistry();
        _resourceRegistry = new ResourceRegistry(
            Substitute.For<ILogger<ResourceRegistry>>(),
            _messengerService,
            ProjectTreeBuilderTestHelper.Build(_projectFolderPath, iconService),
            ResourceClassifierTestHelper.BuildClassifier(),
            _rootHandlerRegistry,
            TestFileSystem.CreateLocal());
        _resourceRegistry.InitializeProjectRoot(_projectFolderPath);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);
        resourceService.RootHandlers.Returns(_rootHandlerRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspacePageLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            _messengerService,
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);

        var sidecarService = new SidecarService(_workspaceWrapper);
        resourceService.Sidecars.Returns(sidecarService);

        _command = new InspectCommand(_workspaceWrapper);
    }

    [TearDown]
    public void TearDown()
    {
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
    public async Task SingleResource_ReturnsArrayOfOne()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"), "Notes.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md.cel"),
            "title = \"Notes\"\n_tags = [\"draft\"]\n");
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        _command.Resources = new[] { new ResourceKey("notes.md") };
        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.Records.Should().HaveCount(1);
        var record = _command.ResultValue.Records[0];
        record.Resource.Should().Be(new ResourceKey("notes.md"));
        record.Status.Should().Be(SidecarStatus.Healthy);
        record.Tags.Should().BeEquivalentTo(new[] { "draft" });
        record.Fields.Should().NotBeNull();
        record.Fields!.Select(f => f.Name).Should().Contain("title");
    }

    [Test]
    public async Task MultipleResources_ReturnsArrayOfN()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.md"), "A.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.md"), "B.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.md.cel"), "title = \"A\"\n");
        // b has no sidecar — should surface as NoSidecar.
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        _command.Resources = new[]
        {
            new ResourceKey("a.md"),
            new ResourceKey("b.md"),
        };
        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.Records.Should().HaveCount(2);
        var byResource = _command.ResultValue.Records
            .ToDictionary(r => r.Resource.ToString(), r => r.Status);
        byResource["project:a.md"].Should().Be(SidecarStatus.Healthy);
        byResource["project:b.md"].Should().Be(SidecarStatus.NoSidecar);
    }

    [Test]
    public async Task Pattern_ReturnsGlobbedSet()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.md"), "A.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.md"), "B.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "skip.txt"), "X.\n");
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        _command.Pattern = "*.md";
        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var resources = _command.ResultValue.Records.Select(r => r.Resource.ToString()).ToList();
        resources.Should().Contain("project:a.md");
        resources.Should().Contain("project:b.md");
        resources.Should().NotContain("project:skip.txt");
    }

    [Test]
    public async Task WholeProject_IncludesNoSidecarForResourcesWithoutSidecar()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "photo.png"), "binary");
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var photo = _command.ResultValue.Records
            .FirstOrDefault(r => r.Resource.Equals(new ResourceKey("photo.png")));
        photo.Should().NotBeNull();
        photo!.Status.Should().Be(SidecarStatus.NoSidecar);
    }

    [Test]
    public async Task SummaryOnly_TrimsTagsAndFields_KeepsStatus()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"), "Notes.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md.cel"),
            "title = \"Notes\"\n_tags = [\"draft\"]\n");
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        _command.Resources = new[] { new ResourceKey("notes.md") };
        _command.SummaryOnly = true;
        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var record = _command.ResultValue.Records.Single();
        record.Status.Should().Be(SidecarStatus.Healthy);
        record.Tags.Should().BeNull();
        record.Fields.Should().BeNull();
    }

    [Test]
    public async Task BrokenSidecar_ReportsBrokenStatusWithParseError()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "doc.md"), "Body.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "doc.md.cel"),
            "this = is not valid = toml ###\n");
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        _command.Resources = new[] { new ResourceKey("doc.md") };
        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var record = _command.ResultValue.Records.Single();
        record.Status.Should().Be(SidecarStatus.Broken);
        record.ParseError.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task OrphanSidecar_ReportsOrphanAgainstSidecarKey()
    {
        // Parent png is absent. Only the orphan sidecar exists.
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png.cel"),
            "_tags = [\"orphan\"]\n");
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var record = _command.ResultValue.Records
            .FirstOrDefault(r => r.Resource.Equals(new ResourceKey("foo.png.cel")));
        record.Should().NotBeNull();
        record!.Status.Should().Be(SidecarStatus.Orphan);
    }

    [Test]
    public async Task InvalidSidecar_DoubleCelSuffix_ReportsInvalidSidecar()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "weird.cel.cel"),
            "_tags = [\"x\"]\n");
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var record = _command.ResultValue.Records
            .FirstOrDefault(r => r.Resource.Equals(new ResourceKey("weird.cel.cel")));
        record.Should().NotBeNull();
        record!.Status.Should().Be(SidecarStatus.InvalidSidecar);
    }

    [Test]
    public async Task SummaryCounts_AddUpToResultsLength()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.md"), "A.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.md.cel"), "title = \"A\"\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.md"), "B.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "broken.md"), "Body.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "broken.md.cel"),
            "this = is not = valid ###");
        File.WriteAllText(Path.Combine(_projectFolderPath, "orphan.png.cel"),
            "_tags = []\n");
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var summary = _command.ResultValue.Summary;
        var totalCount = summary.Healthy
            + summary.Broken
            + summary.Orphan
            + summary.InvalidSidecar
            + summary.NoSidecar;
        totalCount.Should().Be(_command.ResultValue.Records.Count);

        summary.Healthy.Should().BeGreaterThanOrEqualTo(1);
        summary.Broken.Should().BeGreaterThanOrEqualTo(1);
        summary.Orphan.Should().BeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task ReservedFields_HiddenFromFieldsArray_TagsSurfacedSeparately()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"), "Notes.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md.cel"),
            "title = \"Notes\"\n_tags = [\"draft\", \"meeting\"]\n");
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        _command.Resources = new[] { new ResourceKey("notes.md") };
        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var record = _command.ResultValue.Records.Single();
        record.Tags.Should().BeEquivalentTo(new[] { "draft", "meeting" });
        record.Fields.Should().NotBeNull();
        record.Fields!.Select(f => f.Name).Should().NotContain("_tags");
        record.Fields!.Select(f => f.Name).Should().Contain("title");
    }

    [Test]
    public async Task UnknownUnderscoreField_ExcludedFromFieldsArray()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"), "Notes.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md.cel"),
            "title = \"Notes\"\n_unknown = \"hand-edited\"\n");
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        _command.Resources = new[] { new ResourceKey("notes.md") };
        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var record = _command.ResultValue.Records.Single();
        record.Fields.Should().NotBeNull();
        record.Fields!.Select(f => f.Name).Should().NotContain("_unknown");
        record.Fields!.Select(f => f.Name).Should().Contain("title");
    }

    [Test]
    public async Task Pattern_PathAnchoredGlob_MatchesScopedFolder()
    {
        // Regression: PathGlobToRegex produces a regex anchored to the bare
        // path ("foo/bar.md"), not the canonical "project:foo/bar.md" form.
        // Matching against entry.Resource.ToString() left path-anchored
        // patterns returning empty. Switching to entry.Resource.Path fixes it.
        Directory.CreateDirectory(Path.Combine(_projectFolderPath, "scoped"));
        File.WriteAllText(Path.Combine(_projectFolderPath, "scoped", "a.md"), "A.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "scoped", "b.md"), "B.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "outside.md"), "X.\n");
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        _command.Pattern = "scoped/**";
        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var resources = _command.ResultValue.Records.Select(r => r.Resource.ToString()).ToList();
        resources.Should().Contain("project:scoped/a.md");
        resources.Should().Contain("project:scoped/b.md");
        resources.Should().NotContain("project:outside.md");
    }

    [Test]
    public async Task Pattern_DoubleStar_DoesNotDuplicateSidecarAndParent()
    {
        // Regression: the pattern scope used to walk every file resource
        // (including healthy .cel sidecars) so "**" produced one record per
        // parent AND one per sidecar. Pattern mode now filters over the same
        // candidate set as whole-project, so the universe matches.
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"), "Notes.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md.cel"),
            "title = \"Notes\"\n");
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        _command.Pattern = "**";
        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var resources = _command.ResultValue.Records.Select(r => r.Resource.ToString()).ToList();
        resources.Should().Contain("project:notes.md");
        resources.Should().NotContain("project:notes.md.cel");
    }

    [Test]
    public async Task Pattern_DoubleStar_SurfacesOrphanSidecarUnderItsOwnKey()
    {
        // The whole-project candidate set explicitly includes orphans (and
        // other attention-state sidecars) under their own key, so the **
        // pattern surfaces them too.
        File.WriteAllText(Path.Combine(_projectFolderPath, "ghost.png.cel"),
            "_tags = [\"orphan\"]\n");
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        _command.Pattern = "**";
        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var orphan = _command.ResultValue.Records
            .FirstOrDefault(r => r.Resource.Equals(new ResourceKey("ghost.png.cel")));
        orphan.Should().NotBeNull();
        orphan!.Status.Should().Be(SidecarStatus.Orphan);
    }
}
