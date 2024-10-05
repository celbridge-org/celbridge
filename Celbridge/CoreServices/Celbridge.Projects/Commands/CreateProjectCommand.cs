using Celbridge.Commands;
using Celbridge.Navigation;
using Celbridge.Projects.Services;
using Celbridge.Workspace;

using Path = System.IO.Path;

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
        await ProjectUtils.UnloadProjectAsync(_workspaceWrapper, _navigationService, _projectService);

        //
        // Ensure the parent folder exists
        //

        // Create the new project
        var createResult = await CreateProjectAsync(Config);
        if (createResult.IsFailure)
        {
            return Result.Fail($"Failed to create new project. {createResult.Error}");
        }

        // Load the newly created project
        _commandService.Execute<ILoadProjectCommand>(command =>
        {
            command.ProjectFilePath = Config.ProjectFilePath;
        });

        return Result.Ok();
    }

    private async Task<Result> CreateProjectAsync(NewProjectConfig projectConfig)
    {
        try
        {
            var createResult = await _projectService.CreateProjectAsync(projectConfig);
            if (createResult.IsSuccess)
            {
                return Result.Ok();
            }

            return Result.Fail($"Failed to create new project. {createResult.Error}");
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, "An exception occurred when creating the project.");
        }
    }

    //
    // Static methods for scripting support.
    //

    public static void CreateProject(string projectFilePath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ICreateProjectCommand>(command =>
        {
            command.Config = new NewProjectConfig(projectFilePath);
        });
    }
}
