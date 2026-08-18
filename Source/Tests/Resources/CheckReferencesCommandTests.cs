using Celbridge.Tests.Localization;
using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for CheckReferencesCommand, which runs the on-demand ResourceScanner over the project's text
/// files and reports the project: references that do not resolve. The sidecar half of project health
/// lives on the registry's sidecar report and is covered by the sidecar tests.
/// </summary>
[TestFixture]
public class CheckReferencesCommandTests
{
    private string _projectFolderPath = null!;
    private ResourceRegistry _resourceRegistry = null!;
    private RootHandlerRegistry _rootHandlerRegistry = null!;
    private IMessengerService _messengerService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IProjectService _projectService = null!;
    private ICommandService _commandService = null!;
    private IReportWriter _reportWriter = null!;
    private CheckReferencesCommand _command = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(CheckReferencesCommandTests),
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

        var scanner = new ResourceScanner(
            Substitute.For<ILogger<ResourceScanner>>(),
            _workspaceWrapper);
        resourceService.Scanner.Returns(scanner);

        var project = Substitute.For<IProject>();
        project.ProjectFilePath.Returns(Path.Combine(_projectFolderPath, "Project.celbridge"));

        _projectService = Substitute.For<IProjectService>();
        _projectService.CurrentProject.Returns(project);

        _commandService = Substitute.For<ICommandService>();

        _reportWriter = new ReportWriter(
            TestFileSystem.CreateLocal(),
            Substitute.For<ILogger<ReportWriter>>());

        _command = new CheckReferencesCommand(
            _workspaceWrapper,
            _projectService,
            _commandService,
            _reportWriter,
            Substitute.For<ILogger<CheckReferencesCommand>>(),
            new TestLocalizerService());
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
    public async Task CleanProject_ReportsNoBrokenReferences()
    {
        // Fixture uses .json because the scanner only walks allowlisted
        // data-bearing extensions. See ResourceScanner.ScannableExtensions.
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.json"), "{}");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.json"),
            "{ \"target\": \"project:a.json\" }");

        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenReferences.Should().BeEmpty();
        _command.ResultValue.CheckedTargetCount.Should().Be(1);
    }

    [Test]
    public async Task BrokenReference_IsReportedWithSourceAndTarget()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.json"),
            "{ \"target\": \"project:missing.json\" }");

        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenReferences.Should().HaveCount(1);
        var entry = _command.ResultValue.BrokenReferences[0];
        entry.Source.Should().Be(new ResourceKey("source.json"));
        entry.MissingTarget.Should().Be(new ResourceKey("missing.json"));
    }

    [Test]
    public async Task BrokenReference_CarriesThePositionOfTheReferenceLiteral()
    {
        // The literal sits on the third line, and the column is where its "project:" marker starts.
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.json"),
            "{\n  \"a\": 1,\n  \"target\": \"project:missing.json\"\n}");

        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var site = _command.ResultValue.BrokenReferences.Should().ContainSingle().Subject.Site;
        site.Source.Should().Be(new ResourceKey("source.json"));
        site.Line.Should().Be(3);
        site.Column.Should().Be(14);
    }

    [Test]
    public async Task TheSameMissingTargetTwiceInOneFile_IsTwoFindings()
    {
        // Each reference is a separate place to fix, so the index holds one entry per literal rather
        // than one per file.
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.json"),
            "{\n  \"a\": \"project:missing.json\",\n  \"b\": \"project:missing.json\"\n}");

        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var lines = _command.ResultValue.BrokenReferences
            .Select(brokenReference => brokenReference.Site.Line)
            .ToList();
        lines.Should().Equal(2, 3);
    }

    [Test]
    public async Task ReportFindings_CarryTheCodeAndTheLocationToOpenAt()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.json"),
            "{\n  \"target\": \"project:missing.json\"\n}");

        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        _command.OpenReport = true;

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var report = ReadWrittenReport();

        var findings = report.RootElement.GetProperty("sections")
            .EnumerateArray()
            .Single(section => section.GetProperty("kind").GetString() == "findings");

        var item = findings.GetProperty("items")[0];
        item.GetProperty("code").GetString().Should().Be(ReportFindingCatalog.Resource.MissingReference.Code);

        var location = item.GetProperty("actions")[0].GetProperty("location");
        location.GetProperty("line").GetInt32().Should().Be(2);
        location.GetProperty("column").GetInt32().Should().Be(14);
    }

    [Test]
    public async Task TheSummaryCountsBrokenReferencesAndMissingResourcesSeparately()
    {
        // One missing resource named by two references. Reporting either count as the other would
        // overstate how much is actually wrong, or how much there is to fix.
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.json"),
            "{\n  \"a\": \"project:missing.json\",\n  \"b\": \"project:missing.json\"\n}");

        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        _command.OpenReport = true;

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var report = ReadWrittenReport();

        report.RootElement.GetProperty("summary").GetString()
            .Should().Be("2 references point at 1 missing resource.");

        var facts = report.RootElement.GetProperty("sections")
            .EnumerateArray()
            .Single(section => section.GetProperty("kind").GetString() == "facts");

        var values = facts.GetProperty("items")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("message").GetString()!,
                item => item.GetProperty("value").GetString());

        values["Missing resources"].Should().Be("1");
        values["Broken references"].Should().Be("2");
    }

    [Test]
    public async Task AProjectWithNoReferences_SaysSoRatherThanClaimingEveryReferenceResolved()
    {
        _command.OpenReport = true;

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var report = ReadWrittenReport();

        report.RootElement.GetProperty("summary").GetString()
            .Should().Be("No resource references were found to check.");
    }

    private JsonDocument ReadWrittenReport()
    {
        var reportsFolderPath = Path.Combine(_projectFolderPath, ".celbridge", "logs", "reports");
        var reportFilePath = Path.Combine(reportsFolderPath, $"{CheckReferencesCommand.ReportId}.report");

        return JsonDocument.Parse(File.ReadAllText(reportFilePath));
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

        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

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

        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.BrokenReferences.Should().ContainSingle()
            .Which.Source.Should().Be(new ResourceKey("notes.md.cel"));
    }

    [Test]
    public async Task MultipleBrokenReferences_OrderedDeterministically()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.json"),
            "{ \"a\": \"project:zzz.json\", \"b\": \"project:aaa.json\" }");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.json"),
            "{ \"target\": \"project:zzz.json\" }");

        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        // Three entries: aaa.json from a.json. zzz.json from a.json and b.json.
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
    public async Task OpenReport_WritesTheReportAndOpensIt()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.json"),
            "{ \"target\": \"project:missing.json\" }");

        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        _command.OpenReport = true;

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        var reportsFolderPath = Path.Combine(_projectFolderPath, ".celbridge", "logs", "reports");
        var reportFiles = Directory.GetFiles(reportsFolderPath, "*.report");
        reportFiles.Select(Path.GetFileName)
            .Should().Equal($"{CheckReferencesCommand.ReportId}.report");

        _commandService.Received(1).Execute<IOpenDocumentCommand>(
            Arg.Any<Action<IOpenDocumentCommand>>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public async Task WithoutOpenReport_NothingIsWritten()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.json"),
            "{ \"target\": \"project:missing.json\" }");

        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        Directory.Exists(Path.Combine(_projectFolderPath, ".celbridge")).Should().BeFalse();

        _commandService.DidNotReceive().Execute<IOpenDocumentCommand>(
            Arg.Any<Action<IOpenDocumentCommand>>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }
}
