using Celbridge.Commands;
using Celbridge.ProjectSettings.ViewModels;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Reports;
using Celbridge.Workspace;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Covers the Information section: the project version box, and the buttons that open the data folder and the
/// load report.
/// </summary>
[TestFixture]
public class InformationSectionViewModelTests
{
    private ICommandService _commandService = null!;
    private IProjectLoadReporter _projectLoadReporter = null!;
    private ProjectSettingsContext _context = null!;
    private bool _edited;

    [SetUp]
    public void Setup()
    {
        _edited = false;

        _commandService = Substitute.For<ICommandService>();
        _projectLoadReporter = Substitute.For<IProjectLoadReporter>();

        _context = new ProjectSettingsContext(
            Substitute.For<IWorkspaceWrapper>(),
            Substitute.For<IProjectService>(),
            _commandService,
            () => _edited = true);
    }

    [Test]
    public void Load_ProjectWithNoVersion_ShowsTheDefaultAsThePlaceholder()
    {
        var viewModel = CreateViewModel(projectVersion: null);

        viewModel.Load();

        viewModel.ProjectVersionText.Should().BeEmpty();
        viewModel.ProjectVersionPlaceholder.Should().Be("1.0.0");
        viewModel.IsProjectVersionInvalid.Should().BeFalse("an empty box is the default version");
        _edited.Should().BeFalse();
    }

    [Test]
    public void EditingTheProjectVersion_ToAValidVersion_WritesIt()
    {
        var viewModel = CreateViewModel(projectVersion: "1.0.0");
        viewModel.Load();

        viewModel.ProjectVersionText = "1.2.0";

        viewModel.IsProjectVersionInvalid.Should().BeFalse();
        _edited.Should().BeTrue();
        _context.Draft!.ToConfig().Celbridge.ProjectVersion.Should().Be("1.2.0");
    }

    [Test]
    public void EditingTheProjectVersion_ToAMalformedVersion_IsInvalidAndWritesNothing()
    {
        var viewModel = CreateViewModel(projectVersion: "1.0.0");
        viewModel.Load();

        viewModel.ProjectVersionText = "1.2";

        viewModel.IsProjectVersionInvalid.Should().BeTrue();
        _edited.Should().BeFalse();
        _context.Draft!.ToConfig().Celbridge.ProjectVersion.Should().Be("1.0.0");
    }

    [Test]
    public void ClearingTheProjectVersion_RemovesTheKey()
    {
        var viewModel = CreateViewModel(projectVersion: "1.2.0");
        viewModel.Load();

        viewModel.ProjectVersionText = string.Empty;

        viewModel.IsProjectVersionInvalid.Should().BeFalse();
        _context.Draft!.Serialize().Should().NotContain("project-version");
    }

    [Test]
    public void OpenProjectData_RunsTheOpenProjectDataCommand()
    {
        var viewModel = CreateViewModel(projectVersion: null);

        viewModel.OpenProjectDataCommand.Execute(null);

        _commandService.Received(1).Execute<IOpenProjectDataCommand>(
            Arg.Any<Action<IOpenProjectDataCommand>>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public void OpenLoadReport_AfterTheReportIsWritten_OpensIt()
    {
        var reportResource = new ResourceKey("logs:reports/project-load.report");
        _projectLoadReporter.WrittenReport.Returns(new ProjectLoadReportSummary(reportResource, ReportSeverity.Info, 0));
        Action<IOpenDocumentCommand>? configure = null;
        _commandService.Execute(
            Arg.Do<Action<IOpenDocumentCommand>>(action => configure = action),
            Arg.Any<string>(),
            Arg.Any<int>());
        var viewModel = CreateViewModel(projectVersion: null);

        viewModel.OpenLoadReportCommand.Execute(null);

        configure.Should().NotBeNull();
        var openCommand = Substitute.For<IOpenDocumentCommand>();
        configure!(openCommand);
        openCommand.FileResource.Should().Be(reportResource);
    }

    [Test]
    public void OpenLoadReport_NoReportWritten_OpensNothing()
    {
        _projectLoadReporter.WrittenReport.Returns((ProjectLoadReportSummary?)null);
        var viewModel = CreateViewModel(projectVersion: null);

        viewModel.OpenLoadReportCommand.Execute(null);

        _commandService.DidNotReceive().Execute<IOpenDocumentCommand>(
            Arg.Any<Action<IOpenDocumentCommand>>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    private InformationSectionViewModel CreateViewModel(string? projectVersion)
    {
        var config = new ProjectConfig
        {
            Celbridge = new CelbridgeSection
            {
                CelbridgeVersion = "1.0.0",
                ProjectVersion = projectVersion
            }
        };

        _context.Draft = new ProjectConfigDraft(config);

        return new InformationSectionViewModel(_context, _projectLoadReporter);
    }
}
