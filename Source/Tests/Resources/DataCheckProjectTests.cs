using Celbridge.Explorer.Services;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Helpers;
using Celbridge.Resources.Services;
using Celbridge.Resources.Services.Roots;
using Celbridge.Tests.FileSystem;
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
    private string _logsBackingFolder = null!;
    private ResourceRegistry _resourceRegistry = null!;
    private RootHandlerRegistry _rootHandlerRegistry = null!;
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

        _logsBackingFolder = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(DataCheckProjectTests) + "_logs",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_logsBackingFolder);

        _messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        _rootHandlerRegistry = new RootHandlerRegistry();
        _resourceRegistry = new ResourceRegistry(
            Substitute.For<ILogger<ResourceRegistry>>(),
            _messengerService,
            new ProjectTreeBuilder(fileIconService),
            ResourceClassifierTestHelper.BuildClassifierWithNoFactories(),
            _rootHandlerRegistry,
            TestFileSystem.CreateLocal());
        _resourceRegistry.InitializeProjectRoot(_projectFolderPath);

        // ProjectCheckCommand writes its latest report to logs:project-check.log,
        // so the gateway needs a logs: root or the write step fails.
        _rootHandlerRegistry.RegisterRootHandler(
            new LogsRootHandler(_logsBackingFolder));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);
        resourceService.RootHandlerRegistry.Returns(_rootHandlerRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspacePageLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            _messengerService,
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        workspaceService.ResourceFileSystem.Returns(resourceFileSystem);

        var scanner = new ResourceScanner(
            Substitute.For<ILogger<ResourceScanner>>(),
            _workspaceWrapper);
        workspaceService.ResourceScanner.Returns(scanner);

        _command = new ProjectCheckCommand(
            Substitute.For<ILogger<ProjectCheckCommand>>(),
            _workspaceWrapper);
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
        if (Directory.Exists(_logsBackingFolder))
        {
            try
            {
                Directory.Delete(_logsBackingFolder, true);
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
        // Fixture uses .json because the scanner only walks allowlisted
        // data-bearing extensions. See ResourceScanner.ScannableExtensions.
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.json"), "{}");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.json"),
            "{ \"target\": \"project:a.json\" }");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenReferences.Should().BeEmpty();
        _command.ResultValue.OrphanCelFiles.Should().BeEmpty();
        _command.ResultValue.BrokenCelFiles.Should().BeEmpty();
    }

    [Test]
    public async Task BrokenReference_IsReportedWithSourceAndTarget()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.json"),
            "{ \"target\": \"project:missing.json\" }");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenReferences.Should().HaveCount(1);
        var entry = _command.ResultValue.BrokenReferences[0];
        entry.Source.Should().Be(new ResourceKey("source.json"));
        entry.MissingTarget.Should().Be(new ResourceKey("missing.json"));
    }

    [Test]
    public async Task NonAllowlistedExtensions_AreExcludedFromScan()
    {
        // .md is not on the allowlist (along with .txt, .rst, .yaml, and every
        // other extension not enumerated in ResourceScanner.ScannableExtensions).
        // A "project:..." literal inside an off-allowlist file is treated as
        // descriptive prose, not as an active reference — no cascade rewrite,
        // no broken-reference detection. This test guards the allowlist gate
        // using markdown as a representative example.
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"),
            "This documentation mentions \"project:missing.json\" but it should NOT be tracked.");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenReferences.Should().BeEmpty();
    }

    [Test]
    public async Task SidecarOfNonAllowlistedParent_IsStillScanned()
    {
        // A .cel sidecar attached to a parent whose extension is NOT on the
        // allowlist (e.g. notes.md.cel next to notes.md) carries the .cel
        // extension under Path.GetExtension, NOT the parent's .md extension.
        // The allowlist is keyed on file extension, not on parent — sidecars
        // are data regardless of what they're paired with, so they continue
        // to participate in reference scanning even when their parent file
        // would be skipped on its own.
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"),
            "Body.");
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md.cel"),
            "editor = \"celbridge.notes\"\nlink = \"project:missing.json\"\n");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenReferences.Should().ContainSingle()
            .Which.Source.Should().Be(new ResourceKey("notes.md.cel"));
    }

    [Test]
    public async Task OrphanCelFile_AppearsInReport()
    {
        // foo.png is the would-be parent; only the sidecar exists.
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png.cel"),
            "tags = [\"orphaned\"]\n");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.OrphanCelFiles
            .Should().Contain(new ResourceKey("foo.png.cel"));
    }

    [Test]
    public async Task BrokenCelFile_AppearsInReport()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "doc.md"), "Body.");
        File.WriteAllText(Path.Combine(_projectFolderPath, "doc.md.cel"),
            "this = is not valid = toml ###\n");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenCelFiles
            .Should().Contain(new ResourceKey("doc.md.cel"));
    }

    [Test]
    public async Task InvalidCelSuffix_AppearsInBrokenList()
    {
        // .cel.cel files are classified Broken per the sidecar pairing rules.
        File.WriteAllText(Path.Combine(_projectFolderPath, "weird.cel.cel"),
            "tags = [\"x\"]\n");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenCelFiles
            .Should().Contain(new ResourceKey("weird.cel.cel"));
    }

    [Test]
    public async Task MultipleBrokenReferences_OrderedDeterministically()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.json"),
            "{ \"a\": \"project:zzz.json\", \"b\": \"project:aaa.json\" }");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.json"),
            "{ \"target\": \"project:zzz.json\" }");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        // Three entries: aaa.json from a.json; zzz.json from a.json and b.json.
        // The ordering is by missingTarget then by source.
        _command.ResultValue.BrokenReferences.Should().HaveCount(3);

        var keys = _command.ResultValue.BrokenReferences
            .Select(r => (r.MissingTarget.ToString(), r.Source.ToString()))
            .ToList();

        keys[0].Item1.Should().Be("project:aaa.json");
        keys[1].Item1.Should().Be("project:zzz.json");
        keys[2].Item1.Should().Be("project:zzz.json");
        keys[1].Item2.Should().Be("project:a.json");
        keys[2].Item2.Should().Be("project:b.json");
    }

    [Test]
    public async Task ReportFile_IsWrittenToLogsRoot_WithFindings()
    {
        // The report file is the durable artifact the UI will eventually link
        // to from the project-check warning banner; the command rewrites it on
        // every run.
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.json"),
            "{ \"target\": \"project:missing.json\" }");
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png.cel"),
            "tags = [\"orphaned\"]\n");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var reportPath = Path.Combine(_logsBackingFolder, "project-check.log");
        File.Exists(reportPath).Should().BeTrue();

        var reportText = File.ReadAllText(reportPath);
        reportText.Should().Contain("Project consistency check");
        reportText.Should().Contain("Broken references (1):");
        reportText.Should().Contain("project:source.json");
        reportText.Should().Contain("project:missing.json");
        reportText.Should().Contain("Orphan .cel files (1):");
        reportText.Should().Contain("project:foo.png.cel");
    }

    [Test]
    public async Task ReportFile_IsWrittenToLogsRoot_NoFindings()
    {
        // Clean projects still produce a file so the eventual "open report"
        // button is never broken.
        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var reportPath = Path.Combine(_logsBackingFolder, "project-check.log");
        File.Exists(reportPath).Should().BeTrue();

        var reportText = File.ReadAllText(reportPath);
        reportText.Should().Contain("Project consistency check");
        reportText.Should().Contain("No findings.");
    }
}
