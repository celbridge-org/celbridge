using System.Globalization;
using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Projects;
using Celbridge.Projects.Commands;
using Celbridge.Tests.Localization;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Projects;

[TestFixture]
public class CreateProjectCommandTests
{
    private ICommandService _commandService = null!;
    private IProjectService _projectService = null!;
    private IDialogService _dialogService = null!;
    private IStringLocalizer _stringLocalizer = null!;
    private NewProjectConfig _config = null!;

    [SetUp]
    public void Setup()
    {
        _commandService = Substitute.For<ICommandService>();
        _projectService = Substitute.For<IProjectService>();
        _dialogService = Substitute.For<IDialogService>();

        // Backed by the application's own resources, so a key with no entry shows up as the bare key
        // name in the dialog text the assertions read.
        var strings = TestLocalizerService.LoadStrings();
        _stringLocalizer = Substitute.For<IStringLocalizer>();

        _stringLocalizer[Arg.Any<string>()].Returns(callInfo =>
        {
            var name = (string)callInfo[0];

            return ResolveString(strings, name);
        });

        _stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(callInfo =>
        {
            var name = (string)callInfo[0];
            var arguments = (object[])callInfo[1];
            var localizedString = ResolveString(strings, name);
            var formattedValue = string.Format(CultureInfo.InvariantCulture, localizedString.Value, arguments);

            return new LocalizedString(name, formattedValue, localizedString.ResourceNotFound);
        });

        var template = new ProjectTemplate
        {
            Id = "Empty",
            Name = "Empty Project",
            Description = "An empty project",
            Icon = "bs-file-earmark"
        };
        _config = new NewProjectConfig("/projects/MyProject/MyProject.celbridge", template);

        _projectService.CreateProjectAsync(Arg.Any<NewProjectConfig>(), Arg.Any<bool>())
            .Returns(Result.Ok());
    }

    [Test]
    public async Task NoConflictingFiles_CreatesWithoutAsking()
    {
        GivenConflictingFileNames();

        var command = CreateCommand();
        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        await _dialogService.DidNotReceive().ShowConfirmationDialogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ConfirmationDialogOptions>());
        // Nothing was approved, so nothing may be replaced.
        await _projectService.Received(1).CreateProjectAsync(_config, false);
    }

    [Test]
    public async Task ConflictingFiles_Confirmed_CreatesProject()
    {
        GivenConflictingFileNames("readme.md");
        GivenConfirmationAnswer(true);

        var command = CreateCommand();
        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        // Replacement is requested only because the user just agreed to it.
        await _projectService.Received(1).CreateProjectAsync(_config, true);
    }

    [Test]
    public async Task ConflictingFiles_Declined_LeavesTheOpenProjectAlone()
    {
        GivenConflictingFileNames("readme.md");
        GivenConfirmationAnswer(false);

        var command = CreateCommand();
        var result = await command.ExecuteAsync();

        // Declining is an outcome, not an error.
        result.IsSuccess.Should().BeTrue();

        await _projectService.DidNotReceive().CreateProjectAsync(Arg.Any<NewProjectConfig>(), Arg.Any<bool>());

        // The confirmation comes before the open project is closed, so declining leaves the user
        // with the project they already had rather than dropping them on Home.
        await _commandService.DidNotReceive().ExecuteImmediate<IUnloadProjectCommand>(
            Arg.Any<Action<IUnloadProjectCommand>?>(), Arg.Any<string>(), Arg.Any<int>());
    }

    [Test]
    public async Task ConflictingFiles_AreCountedInTheConfirmation()
    {
        GivenConflictingFileNames("hello_world.py", "readme.md");
        GivenConfirmationAnswer(false);

        var command = CreateCommand();
        await command.ExecuteAsync();

        var confirmationArguments = GetConfirmationArguments();
        var titleText = (string)confirmationArguments[0]!;
        var messageText = (string)confirmationArguments[1]!;

        // Several files are counted rather than listed, so the message stays one sentence to translate.
        titleText.Should().Be("Replace");
        messageText.Should().Be("This will replace 2 existing files. This cannot be undone.");
    }

    [Test]
    public async Task OneConflictingFile_IsDescribedInTheSingular()
    {
        GivenConflictingFileNames("readme.md");
        GivenConfirmationAnswer(false);

        var command = CreateCommand();
        await command.ExecuteAsync();

        var confirmationArguments = GetConfirmationArguments();
        var titleText = (string)confirmationArguments[0]!;
        var messageText = (string)confirmationArguments[1]!;

        // A single file is named, and the wording agrees with it: the plural reads wrongly over one name,
        // which is the common case the reported issue hit.
        titleText.Should().Be("Replace");
        messageText.Should().Be("This will replace the existing file 'readme.md'. This cannot be undone.");
    }

    private object?[] GetConfirmationArguments()
    {
        var confirmationCall = _dialogService.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IDialogService.ShowConfirmationDialogAsync));

        return confirmationCall.GetArguments();
    }

    [Test]
    public async Task ConflictCheckFails_DoesNotCreateTheProject()
    {
        _projectService.GetConflictingFileNamesAsync(Arg.Any<NewProjectConfig>())
            .Returns(Result<IReadOnlyList<string>>.Fail("Template could not be read."));

        var command = CreateCommand();
        var result = await command.ExecuteAsync();

        // Creating without knowing which files would be replaced risks destroying the user's work.
        result.IsFailure.Should().BeTrue();
        await _projectService.DidNotReceive().CreateProjectAsync(Arg.Any<NewProjectConfig>(), Arg.Any<bool>());

        // The dialog closed when Create was clicked, so silence here would read as success.
        await _dialogService.Received(1).ShowAlertDialogAsync(Arg.Any<string>(), Arg.Any<string>());

        // The check runs before the open project is closed, so a failed check leaves it open.
        await _commandService.DidNotReceive().ExecuteImmediate<IUnloadProjectCommand>(
            Arg.Any<Action<IUnloadProjectCommand>?>(), Arg.Any<string>(), Arg.Any<int>());
    }

    private static LocalizedString ResolveString(IReadOnlyDictionary<string, string> strings, string name)
    {
        var found = strings.TryGetValue(name, out var value);

        return new LocalizedString(name, found ? value! : name, resourceNotFound: !found);
    }

    private CreateProjectCommand CreateCommand()
    {
        var command = new CreateProjectCommand(
            _commandService,
            _projectService,
            Substitute.For<IWorkspaceWrapper>(),
            _dialogService,
            _stringLocalizer);

        command.Config = _config;

        return command;
    }

    private void GivenConflictingFileNames(params string[] fileNames)
    {
        var conflictingFileNames = new List<string>(fileNames);
        _projectService.GetConflictingFileNamesAsync(Arg.Any<NewProjectConfig>())
            .Returns(conflictingFileNames.OkResult<IReadOnlyList<string>>());
    }

    private void GivenConfirmationAnswer(bool confirmed)
    {
        _dialogService.ShowConfirmationDialogAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ConfirmationDialogOptions>())
            .Returns(Result<bool>.Ok(confirmed));
    }
}
