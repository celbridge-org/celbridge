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

        // Close any open project.
        // This will fail if there's no project currently open, but we can just ignore that.
        await _commandService.ExecuteImmediate<IUnloadProjectCommand>();

        // Create the new project
        var createResult = await _projectService.CreateProjectAsync(Config);
        if (createResult.IsFailure)
        {
            // The open project was closed above, so the shell is already showing Home.
            var alertTitle = _stringLocalizer.GetString("CreateProject_FailedTitle");
            var alertMessage = _stringLocalizer.GetString("CreateProject_FailedMessage");
            await _dialogService.ShowAlertDialogAsync(alertTitle, alertMessage);

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
}
