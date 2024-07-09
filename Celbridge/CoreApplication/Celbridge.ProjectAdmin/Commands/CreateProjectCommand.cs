using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Navigation;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.BaseLibrary.Commands;
using Celbridge.ProjectAdmin.Services;

namespace Celbridge.ProjectAdmin.Commands;

public class CreateProjectCommand : CommandBase, ICreateProjectCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly INavigationService _navigationService;
    private readonly ICommandService _commandService;

    public CreateProjectCommand(
        IWorkspaceWrapper workspaceWrapper,
        IProjectDataService projectDataService,
        INavigationService navigationService,
        ICommandService commandService)
    {
        _workspaceWrapper = workspaceWrapper;
        _projectDataService = projectDataService;
        _navigationService = navigationService;
        _commandService = commandService;
    }

    public NewProjectConfig? Config { get; set; }

    public override async Task<Result> ExecuteAsync()
    {
        if (Config is null)
        {
            return Result.Fail("Failed to create new project because config is null.");
        }

        var projectFilePath = Config.ProjectFilePath;

        if (File.Exists(projectFilePath))
        {
            return Result.Fail($"Failed to create project file at '{projectFilePath}' because the file already exists.");
        }

        // Close any open project.
        // This will fail if there's no project currently open, but we can just ignore that.
        await ProjectUtils.UnloadProjectAsync(_workspaceWrapper, _navigationService, _projectDataService);

        // Create the new project
        var createResult = await ProjectUtils.CreateProjectAsync(_projectDataService, Config);
        if (createResult.IsFailure)
        {
            return Result.Fail($"Failed to create new project. {createResult.Error}");
        }

        // Load the new project
        _commandService.Execute<ILoadProjectCommand>(command => command.ProjectFilePath = projectFilePath);

        return Result.Ok();
    }

    public static void CreateProject(string projectName, string folder)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ICreateProjectCommand>(command =>
        {
            command.Config = new NewProjectConfig(projectName, folder);
        });
    }
}
