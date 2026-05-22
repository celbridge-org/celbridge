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
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.md"), "Body A.");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.md"),
            "Refers to \"project:a.md\".");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenReferences.Should().BeEmpty();
        _command.ResultValue.OrphanSidecars.Should().BeEmpty();
        _command.ResultValue.BrokenSidecars.Should().BeEmpty();
    }

    [Test]
    public async Task BrokenReference_IsReportedWithSourceAndTarget()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.md"),
            "Refers to \"project:missing.md\" which is not present.");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenReferences.Should().HaveCount(1);
        var entry = _command.ResultValue.BrokenReferences[0];
        entry.Source.Should().Be(new ResourceKey("source.md"));
        entry.MissingTarget.Should().Be(new ResourceKey("missing.md"));
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
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.md"),
            "Refers \"project:zzz.md\" and \"project:aaa.md\".");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.md"),
            "Also refers \"project:zzz.md\".");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        // Three entries: aaa.md from a.md; zzz.md from a.md and b.md.
        // The ordering is by missingTarget then by source.
        _command.ResultValue.BrokenReferences.Should().HaveCount(3);

        var keys = _command.ResultValue.BrokenReferences
            .Select(r => (r.MissingTarget.ToString(), r.Source.ToString()))
            .ToList();

        keys[0].Item1.Should().Be("aaa.md");
        keys[1].Item1.Should().Be("zzz.md");
        keys[2].Item1.Should().Be("zzz.md");
        keys[1].Item2.Should().Be("a.md");
        keys[2].Item2.Should().Be("b.md");
    }
}
