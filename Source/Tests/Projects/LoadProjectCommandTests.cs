using System.Globalization;
using Celbridge.Automation;
using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Projects;
using Celbridge.Projects.Commands;
using Celbridge.Settings;
using Celbridge.Tests.Helpers;
using Celbridge.Tests.Localization;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Tests the event the load project command reports when a load fails, and the refusal of a project when its
/// folder contains another project file.
/// </summary>
[TestFixture]
public class LoadProjectCommandTests
{
    private const string ProjectFile = "/projects/sample/sample.celbridge";
    private const string OtherProjectFile = "/projects/sample/other.celbridge";

    private ICommandService _commandService = null!;
    private IProjectService _projectService = null!;
    private IProjectLoader _projectLoader = null!;
    private IAutomationService _automationService = null!;
    private IDialogService _dialogService = null!;
    private ISettingsService _settingsService = null!;
    private LoadProjectCommand _command = null!;

    [SetUp]
    public void Setup()
    {
        _commandService = Substitute.For<ICommandService>();
        _projectService = Substitute.For<IProjectService>();
        _projectLoader = Substitute.For<IProjectLoader>();
        _automationService = Substitute.For<IAutomationService>();
        _dialogService = Substitute.For<IDialogService>();
        _settingsService = Substitute.For<ISettingsService>();

        StubFolderProjectFiles(ProjectFile);

        _command = new LoadProjectCommand(
            new NullLogger<LoadProjectCommand>(),
            _commandService,
            _projectService,
            _projectLoader,
            _automationService,
            _dialogService,
            _settingsService,
            CreateStringLocalizer())
        {
            ProjectFilePath = ProjectFile
        };
    }

    [Test]
    public async Task ALoadThatFails_ReportsTheFailedLoad()
    {
        _projectLoader.LoadProjectAsync(ProjectFile).Returns(Task.FromResult<Result>(Result.Fail("The load failed")));

        var result = await _command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        _automationService.Received(1).LogEvent(new ProjectLoadFailedEvent(ProjectFile));
    }

    [Test]
    public async Task ALoaderThatThrows_FailsTheLoadAndReportsIt()
    {
        _projectLoader.LoadProjectAsync(ProjectFile).Returns<Task<Result>>(_ => throw new InvalidOperationException("The loader threw"));

        var result = await _command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        _automationService.Received(1).LogEvent(new ProjectLoadFailedEvent(ProjectFile));
    }

    [Test]
    public async Task ALoadThatSucceeds_ReportsNoFailure()
    {
        _projectLoader.LoadProjectAsync(ProjectFile).Returns(Task.FromResult<Result>(Result.Ok()));

        var result = await _command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        _automationService.DidNotReceive().LogEvent(Arg.Any<ProjectLoadFailedEvent>());
    }

    [Test]
    public async Task AnotherProjectFileInTheFolder_NamesItAndLeavesTheCurrentProjectOpen()
    {
        StubFolderProjectFiles(OtherProjectFile, ProjectFile);

        var result = await _command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        await _dialogService.Received(1).ShowAlertDialogAsync(
            "Another Project File Found",
            Arg.Is<string>(message => message.Contains("\"other.celbridge\"")));
        await _commandService.DidNotReceiveWithAnyArgs().ExecuteImmediate<IUnloadProjectCommand>();
        await _projectLoader.DidNotReceiveWithAnyArgs().LoadProjectAsync(default!);
        _automationService.Received(1).LogEvent(new ProjectLoadFailedEvent(ProjectFile));
    }

    [Test]
    public async Task AnotherProjectFileInTheFolder_ClearsThePreviousProjectWhenItNamesThisProject()
    {
        StubFolderProjectFiles(OtherProjectFile, ProjectFile);
        _settingsService.Get(SettingCatalog.Project.PreviousProject).Returns(ProjectFile);

        await _command.ExecuteAsync();

        _settingsService.Received(1).Set(SettingCatalog.Project.PreviousProject, string.Empty);
    }

    [Test]
    public async Task AnotherProjectFileInTheFolder_KeepsThePreviousProjectWhenItNamesTheOpenProject()
    {
        // The open project stays open, so the next launch still opens it.
        StubFolderProjectFiles(OtherProjectFile, ProjectFile);
        _settingsService.Get(SettingCatalog.Project.PreviousProject).Returns("/projects/open/open.celbridge");

        await _command.ExecuteAsync();

        _settingsService.DidNotReceiveWithAnyArgs().Set(SettingCatalog.Project.PreviousProject, default!);
    }

    private void StubFolderProjectFiles(params string[] projectFilePaths)
    {
        IReadOnlyList<string> projectFiles = projectFilePaths.ToList();
        _projectService.GetProjectFilesAsync(Arg.Any<string>()).Returns(projectFiles.OkResult());
    }

    // Backed by the application's own resources, so a key with no entry shows up as the bare key name in the
    // alert text the assertions read.
    private static IStringLocalizer CreateStringLocalizer()
    {
        var strings = TestLocalizerService.LoadStrings();
        var stringLocalizer = Substitute.For<IStringLocalizer>();

        stringLocalizer[Arg.Any<string>()].Returns(callInfo =>
        {
            var name = (string)callInfo[0];

            return ResolveString(strings, name);
        });

        stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(callInfo =>
        {
            var name = (string)callInfo[0];
            var arguments = (object[])callInfo[1];
            var localizedString = ResolveString(strings, name);
            var formattedValue = string.Format(CultureInfo.InvariantCulture, localizedString.Value, arguments);

            return new LocalizedString(name, formattedValue, localizedString.ResourceNotFound);
        });

        return stringLocalizer;
    }

    private static LocalizedString ResolveString(IReadOnlyDictionary<string, string> strings, string name)
    {
        var found = strings.TryGetValue(name, out var value);

        return new LocalizedString(name, found ? value! : name, resourceNotFound: !found);
    }
}
