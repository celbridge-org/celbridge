using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Server;

namespace Celbridge.Console.Services;

/// <summary>
/// Adds Celbridge's general variables to every console. They name the MCP server's port, the project folder, the
/// folders behind the temp: and logs: roots, and the Celbridge version. A script in any console can then reach the
/// application and the project's files.
/// </summary>
public sealed class WorkspaceEnvironmentContributor : IConsoleEnvironmentContributor
{
    private readonly IAppEnvironment _appEnvironment;
    private readonly IServerService _serverService;
    private readonly IProjectService _projectService;

    public WorkspaceEnvironmentContributor(
        IAppEnvironment appEnvironment,
        IServerService serverService,
        IProjectService projectService)
    {
        _appEnvironment = appEnvironment;
        _serverService = serverService;
        _projectService = projectService;
    }

    public void FilterInheritedEnvironment(IDictionary<string, string> inheritedEnvironment)
    {
    }

    public async Task ContributeAsync(ConsoleSessionContext context, IDictionary<string, string> environment)
    {
        await Task.CompletedTask;

        var project = _projectService.CurrentProject!;
        var environmentInfo = _appEnvironment.GetEnvironmentInfo();

        var celbridgeVersion = environmentInfo.Configuration == "Debug"
            ? $"{environmentInfo.AppVersion} (Debug)"
            : environmentInfo.AppVersion;

        var generalVariables = new Dictionary<string, string>
        {
            ["CELBRIDGE_MCP_PORT"] = _serverService.Port.ToString(),
            ["CELBRIDGE_PROJECT_FOLDER"] = project.ProjectFolderPath,
            ["CELBRIDGE_TEMP_FOLDER"] = Path.Combine(project.ProjectDataFolderPath, ProjectConstants.TempFolder),
            ["CELBRIDGE_LOGS_FOLDER"] = Path.Combine(project.ProjectDataFolderPath, ProjectConstants.LogsFolder),
            ["CELBRIDGE_VERSION"] = celbridgeVersion,
        };

        foreach (var pair in generalVariables)
        {
            // A variable that the provider or the console's own [session.environment] already set keeps its value.
            if (!environment.ContainsKey(pair.Key))
            {
                environment[pair.Key] = pair.Value;
            }
        }
    }
}
