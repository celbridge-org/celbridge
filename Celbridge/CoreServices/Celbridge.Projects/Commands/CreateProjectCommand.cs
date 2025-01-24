using Celbridge.Commands;
using Celbridge.Navigation;
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

        // Close any open project.
        // This will fail if there's no project currently open, but we can just ignore that.
        await _commandService.ExecuteImmediate<IUnloadProjectCommand>();

        // Create the new project
        var createResult = await _projectService.CreateProjectAsync(Config);
        if (createResult.IsFailure)
        {
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

    public static void CreateProject(string projectFilePath)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICreateProjectCommand>(command =>
        {
            command.Config = new NewProjectConfig(projectFilePath);
        });
    }
}
