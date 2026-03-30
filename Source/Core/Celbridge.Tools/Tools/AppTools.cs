using System.Text.Json;
using Celbridge.ApplicationEnvironment;
using Celbridge.Projects;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// General application tools for version info, logging, and alerts.
/// </summary>
[McpServerToolType]
public partial class AppTools : AgentToolBase
{
    private ILogger<AppTools>? _logger;

    public AppTools(IApplicationServiceProvider services) : base(services) { }

    private ILogger<AppTools> Logger => _logger ??= GetRequiredService<ILogger<AppTools>>();

    /// <summary>
    /// Returns the application version string.
    /// </summary>
    /// <returns>A version string in the format "major.minor.patch", e.g. "0.2.5".</returns>
    [McpServerTool(Name = "app_get_version", ReadOnly = true, Idempotent = true)]
    [ToolAlias("app.get_version")]
    public partial string AppVersion()
    {
        var environmentService = GetRequiredService<IEnvironmentService>();
        var environmentInfo = environmentService.GetEnvironmentInfo();
        return environmentInfo.AppVersion;
    }

    /// <summary>
    /// Returns the project status as JSON with isLoaded and projectName fields.
    /// </summary>
    /// <returns>JSON object with fields: isLoaded (bool), projectName (string).</returns>
    [McpServerTool(Name = "app_get_status", ReadOnly = true, Idempotent = true)]
    [ToolAlias("app.get_status")]
    public partial string GetProjectStatus()
    {
        var projectService = GetRequiredService<IProjectService>();
        var currentProject = projectService.CurrentProject;
        if (currentProject is null)
        {
            return JsonSerializer.Serialize(new
            {
                isLoaded = false,
                projectName = ""
            });
        }

        return JsonSerializer.Serialize(new
        {
            isLoaded = true,
            projectName = currentProject.ProjectName
        });
    }

    /// <summary>
    /// Logs an informational message to the application log.
    /// </summary>
    /// <param name="message">The message to log.</param>
    [McpServerTool(Name = "app_log", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.log")]
    public partial void LogInfo(string message)
    {
        Logger.LogInformation(message);
    }

    /// <summary>
    /// Logs a warning message to the application log.
    /// </summary>
    /// <param name="message">The warning message to log.</param>
    [McpServerTool(Name = "app_log_warning", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.log_warning")]
    public partial void LogWarning(string message)
    {
        Logger.LogWarning(message);
    }

    /// <summary>
    /// Logs an error message to the application log.
    /// </summary>
    /// <param name="message">The error message to log.</param>
    [McpServerTool(Name = "app_log_error", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.log_error")]
    public partial void LogError(string message)
    {
        Logger.LogError(message);
    }

    /// <summary>
    /// Forces an immediate refresh of the project file listing.
    /// Only needed when non-Celbridge MCP tools have written files to the project folder directly.
    /// Celbridge tools always keep the file listing up to date automatically.
    /// </summary>
    [McpServerTool(Name = "app_refresh_files", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.refresh_files")]
    public partial CallToolResult RefreshFiles()
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var result = workspaceWrapper.WorkspaceService.ResourceService.UpdateResources();
        if (result.IsFailure)
        {
            return ErrorResult($"Failed to refresh file listing: {result.FirstErrorMessage}");
        }

        return SuccessResult("File listing refreshed.");
    }

    /// <summary>
    /// Shows an alert dialog to the user with a message and optional title.
    /// </summary>
    /// <param name="message">The message to display in the alert dialog.</param>
    /// <param name="title">Optional title for the alert dialog.</param>
    [McpServerTool(Name = "app_show_alert")]
    [ToolAlias("app.show_alert")]
    public async partial Task<CallToolResult> ShowAlert(string message, string title = "")
    {
        return await ExecuteCommandAsync<IAlertCommand>(command =>
        {
            command.Message = message;
            command.Title = title;
        });
    }
}
