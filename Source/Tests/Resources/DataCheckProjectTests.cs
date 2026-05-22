using Celbridge.Explorer.Services;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.UserInterface.Services;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for ProjectCheckCommand — the engine behind the data_check_project
/// MCP tool. The command runs the on-demand ResourceScanner over the project's
/// text files and consults the registry's sidecar report.
/// </summary>
[TestFixture]
public class DataCheckProjectTests
{
    private string _projectFolderPath = null!;
    private ResourceRegistry _resourceRegistry = null!;
    private IMessengerService _messengerService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private ProjectCheckCommand _command = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(DataCheckProjectTests),
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

        var fileSystem = new ResourceFileSystem(
            Substitute.For<ILogger<ResourceFileSystem>>(),
            _messengerService,
            _workspaceWrapper);
        workspaceService.ResourceFileSystem.Returns(fileSystem);

        var scanner = new ResourceScanner(
            Substitute.For<ILogger<ResourceScanner>>(),
            _workspaceWrapper,
            new TextBinarySniffer());
        workspaceService.ResourceScanner.Returns(scanner);

        _command = new ProjectCheckCommand(_workspaceWrapper);
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
    public async Task CleanProject_AllReportListsAreEmpty()
    {
        // Fixture uses .txt because reference scanning excludes documentation
        // file types (.md). See ResourceScanner.ExcludedExtensions for the
        // rationale.
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.txt"), "Body A.");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.txt"),
            "Refers to \"project:a.txt\".");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenReferences.Should().BeEmpty();
        _command.ResultValue.OrphanSidecars.Should().BeEmpty();
        _command.ResultValue.BrokenSidecars.Should().BeEmpty();
    }

    [Test]
    public async Task BrokenReference_IsReportedWithSourceAndTarget()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.txt"),
            "Refers to \"project:missing.txt\" which is not present.");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenReferences.Should().HaveCount(1);
        var entry = _command.ResultValue.BrokenReferences[0];
        entry.Source.Should().Be(new ResourceKey("source.txt"));
        entry.MissingTarget.Should().Be(new ResourceKey("missing.txt"));
    }

    [Test]
    public async Task MarkdownReferences_AreExcludedFromScan()
    {
        // Markdown is documentation, not data. A "project:..." literal inside
        // a .md file is a descriptive mention, not an active reference, so the
        // scanner deliberately skips .md files for both cascade rewrites and
        // broken-reference detection. This test guards that exclusion.
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"),
            "This documentation mentions \"project:missing.md\" but it should NOT be tracked.");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenReferences.Should().BeEmpty();
    }

    [Test]
    public async Task SidecarOfExcludedParent_IsStillScanned()
    {
        // A .cel sidecar attached to an excluded parent (e.g. notes.md.cel
        // next to notes.md) carries the .cel extension under
        // Path.GetExtension, NOT the parent's .md extension. The exclusion
        // policy excludes by file extension, not by parent — sidecars are
        // data regardless of what they're paired with, so they continue to
        // participate in reference scanning even when their parent file is
        // a documentation type.
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"),
            "Body.");
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md.cel"),
            "editor = \"celbridge.notes\"\nlink = \"project:missing.txt\"\n");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenReferences.Should().ContainSingle()
            .Which.Source.Should().Be(new ResourceKey("notes.md.cel"));
    }

    [Test]
    public async Task OrphanSidecar_AppearsInReport()
    {
        // foo.png is the would-be parent; only the sidecar exists.
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png.cel"),
            "tags = [\"orphaned\"]\n");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.OrphanSidecars
            .Should().Contain(o => o.Sidecar == new ResourceKey("foo.png.cel"));
    }

    [Test]
    public async Task BrokenSidecar_AppearsInReport()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "doc.md"), "Body.");
        File.WriteAllText(Path.Combine(_projectFolderPath, "doc.md.cel"),
            "this = is not valid = toml ###\n");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenSidecars
            .Should().Contain(b => b.Sidecar == new ResourceKey("doc.md.cel"));
    }

    [Test]
    public async Task InvalidSidecarSuffix_AppearsInBrokenList()
    {
        // .cel.cel files are classified Broken per the sidecar pairing rules.
        File.WriteAllText(Path.Combine(_projectFolderPath, "weird.cel.cel"),
            "tags = [\"x\"]\n");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenSidecars
            .Should().Contain(b => b.Sidecar == new ResourceKey("weird.cel.cel"));
    }

    [Test]
    public async Task MultipleBrokenReferences_OrderedDeterministically()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.txt"),
            "Refers \"project:zzz.txt\" and \"project:aaa.txt\".");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.txt"),
            "Also refers \"project:zzz.txt\".");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        // Three entries: aaa.txt from a.txt; zzz.txt from a.txt and b.txt.
        // The ordering is by missingTarget then by source.
        _command.ResultValue.BrokenReferences.Should().HaveCount(3);

        var keys = _command.ResultValue.BrokenReferences
            .Select(r => (r.MissingTarget.ToString(), r.Source.ToString()))
            .ToList();

        keys[0].Item1.Should().Be("project:aaa.txt");
        keys[1].Item1.Should().Be("project:zzz.txt");
        keys[2].Item1.Should().Be("project:zzz.txt");
        keys[1].Item2.Should().Be("project:a.txt");
        keys[2].Item2.Should().Be("project:b.txt");
    }
}
