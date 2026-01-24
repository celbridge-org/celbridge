using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Navigation;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Projects.Commands;

public class CreateProjectCommand : CommandBase, ICreateProjectCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;
    private readonly INavigationService _navigationService;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public CreateProjectCommand(
        ICommandService commandService,
        IProjectService projectService,
        INavigationService navigationService,
        IWorkspaceWrapper workspaceWrapper,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _commandService = commandService;
        _projectService = projectService;
        _navigationService = navigationService;
        _workspaceWrapper = workspaceWrapper;
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
            // Show alert dialog and navigate to home page on failure
            var alertTitle = _stringLocalizer.GetString("CreateProject_FailedTitle");
            var alertMessage = _stringLocalizer.GetString("CreateProject_FailedMessage");
            await _dialogService.ShowAlertDialogAsync(alertTitle, alertMessage);

            _navigationService.NavigateToPage(NavigationConstants.HomeTag);

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

    //
    // Static methods for scripting support.
    //

    public static void CreateProject(string projectFilePath, string templateId)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();
        var templateService = ServiceLocator.AcquireService<IProjectTemplateService>();

        var template = templateService.GetTemplates().FirstOrDefault(t => t.Id == templateId);
        if (template is null)
        {
            template = templateService.GetDefaultTemplate();
        }

        commandService.Execute<ICreateProjectCommand>(command =>
        {
            command.Config = new NewProjectConfig(projectFilePath, template);
        });
    }
}

