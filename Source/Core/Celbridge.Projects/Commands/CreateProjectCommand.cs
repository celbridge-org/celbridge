using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Projects.Commands;

public class CreateProjectCommand : CommandBase, ICreateProjectCommand
{
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public CreateProjectCommand(
        ICommandService commandService,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _commandService = commandService;
        _projectService = projectService;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;
    }

    public NewProjectConfig? Config { get; set; }

    public override async Task<Result> ExecuteAsync()
    {
        if (Config is null)
        {
            return Result.Fail("Failed to create new project because config is null.");
        }

        // Settle what happens to any existing files before closing the open project, so that
        // declining the replacement leaves the user with the project they already had.
        var conflictsResult = await _projectService.GetConflictingFileNamesAsync(Config);
        if (conflictsResult.IsFailure)
        {
            // Creating without knowing which files would be replaced risks destroying the user's work.
            await ShowCreateFailedAlertAsync();

            return Result.Fail("Failed to check which files the template would replace.")
                .WithErrors(conflictsResult);
        }

        var conflictingFileNames = conflictsResult.Value;
        if (conflictingFileNames.Count > 0)
        {
            var confirmResult = await ConfirmReplaceFilesAsync(conflictingFileNames);
            if (confirmResult.IsFailure)
            {
                await ShowCreateFailedAlertAsync();

                return Result.Fail("Failed to confirm replacing existing files.")
                    .WithErrors(confirmResult);
            }

            var confirmed = confirmResult.Value;
            if (!confirmed)
            {
                return Result.Ok();
            }
        }

        // Close any open project.
        // This will fail if there's no project currently open, but we can just ignore that.
        await _commandService.ExecuteImmediate<IUnloadProjectCommand>();

        // Create the new project
        var createResult = await _projectService.CreateProjectAsync(Config);
        if (createResult.IsFailure)
        {
            // The open project was closed above, so the shell is already showing Home.
            await ShowCreateFailedAlertAsync();

            return Result.Fail($"Failed to create project.")
                .WithErrors(createResult);
        }

        // Load the newly created project
        _commandService.Execute<ILoadProjectCommand>(command =>
        {
            command.ProjectFilePath = Config.ProjectFilePath;
        });
        return Result.Ok();
    }

    // Names the files the template would replace and asks the user whether to go ahead.
    private async Task<Result<bool>> ConfirmReplaceFilesAsync(IReadOnlyList<string> conflictingFileNames)
    {
        // One file is named, several are counted, so each message stays a single translatable sentence
        // rather than a list glued into one written for the plural.
        string confirmationMessage;
        if (conflictingFileNames.Count == 1)
        {
            var conflictingFileName = conflictingFileNames[0];
            confirmationMessage = _stringLocalizer.GetString("CreateProject_ReplaceFilesMessage_One", conflictingFileName);
        }
        else
        {
            confirmationMessage = _stringLocalizer.GetString("CreateProject_ReplaceFilesMessage_Many", conflictingFileNames.Count);
        }

        // The title and the confirm button carry the verb, as the delete confirmation does.
        var replaceText = _stringLocalizer.GetString("CreateProject_Replace");

        var confirmationOptions = new ConfirmationDialogOptions
        {
            PrimaryButtonText = replaceText,
            IsDestructive = true
        };

        var showResult = await _dialogService.ShowConfirmationDialogAsync(
            replaceText,
            confirmationMessage,
            confirmationOptions);

        if (showResult.IsFailure)
        {
            return Result<bool>.Fail("Failed to show the replace files confirmation dialog.")
                .WithErrors(showResult);
        }

        return showResult.Value;
    }

    private async Task ShowCreateFailedAlertAsync()
    {
        var alertTitle = _stringLocalizer.GetString("CreateProject_FailedTitle");
        var alertMessage = _stringLocalizer.GetString("CreateProject_FailedMessage");
        await _dialogService.ShowAlertDialogAsync(alertTitle, alertMessage);
    }
}
