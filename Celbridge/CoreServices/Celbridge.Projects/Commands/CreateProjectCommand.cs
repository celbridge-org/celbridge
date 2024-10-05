using Celbridge.Commands;
using Celbridge.Navigation;
using Celbridge.Projects.Services;
using Celbridge.Workspace;

namespace Celbridge.Projects.Commands;

public class CreateProjectCommand : CommandBase, ICreateProjectCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;
    private readonly INavigationService _navigationService;
    private readonly ICommandService _commandService;

    public CreateProjectCommand(
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService,
        INavigationService navigationService,
        ICommandService commandService)
    {
        _workspaceWrapper = workspaceWrapper;
        _projectService = projectService;
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
        await ProjectUtils.UnloadProjectAsync(_workspaceWrapper, _navigationService, _projectService);

        // Create the new project
        var createResult = await ProjectUtils.CreateProjectAsync(_projectService, Config);
        if (createResult.IsFailure)
        {
            return Result.Fail($"Failed to create new project. {createResult.Error}");
        }

        // Load the new project
        _commandService.Execute<ILoadProjectCommand>(command => command.ProjectFilePath = projectFilePath);

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void CreateProject(string projectName, string folder, bool createSubfolder)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ICreateProjectCommand>(command =>
        {
            command.Config = new NewProjectConfig(projectName, folder, createSubfolder);
        });
    }
}
