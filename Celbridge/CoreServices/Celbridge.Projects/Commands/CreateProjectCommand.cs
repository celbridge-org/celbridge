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
        ICommandService commandService,
        IProjectService projectService,
        INavigationService navigationService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _commandService = commandService;
        _projectService = projectService;
        _navigationService = navigationService;
        _workspaceWrapper = workspaceWrapper;
    }

    public NewProjectConfig? Config { get; set; }

    public override async Task<Result> ExecuteAsync()
    {
        if (Config is null)
        {
            return Result.Fail("Failed to create new project because config is null.");
        }

        // Change the Navigation Cache status of the active persistent pages to Disabled, to allow them to be destroyed.
        _navigationService.ClearPersistenceOfAllLoadedPages();

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
