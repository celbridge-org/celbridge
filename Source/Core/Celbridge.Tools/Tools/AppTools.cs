using System.Text.Json;
using Celbridge.ApplicationEnvironment;
using Celbridge.Projects;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// General application tools for version info, logging, and client configuration.
/// </summary>
[McpServerToolType]
public partial class AppTools : AgentToolBase
{
    private ILogger<AppTools>? _logger;

    public AppTools(IApplicationServiceProvider services) : base(services) {}

    private ILogger<AppTools> Logger => _logger ??= GetRequiredService<ILogger<AppTools>>();

    /// <summary>
    /// Returns the application version string.
    /// </summary>
    /// <returns>A version string in the format "major.minor.patch", e.g. "0.2.5".</returns>
    [McpServerTool(Name = "app_version", ReadOnly = true, Idempotent = true)]
    [ToolAlias("app.version")]
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
    [McpServerTool(Name = "get_project_status", ReadOnly = true, Idempotent = true)]
    [ToolAlias("app.status")]
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
    [McpServerTool(Name = "log_info", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.log")]
    public partial void LogInfo(string message)
    {
        Logger.LogInformation(message);
    }

    /// <summary>
    /// Logs a warning message to the application log.
    /// </summary>
    /// <param name="message">The warning message to log.</param>
    [McpServerTool(Name = "log_warning", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.log_warning")]
    public partial void LogWarning(string message)
    {
        Logger.LogWarning(message);
    }

    /// <summary>
    /// Logs an error message to the application log.
    /// </summary>
    /// <param name="message">The error message to log.</param>
    [McpServerTool(Name = "log_error", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.log_error")]
    public partial void LogError(string message)
    {
        Logger.LogError(message);
    }

    /// <summary>
    /// Returns context information for AI agents including resource key conventions and project structure.
    /// </summary>
    /// <returns>A Markdown document describing resource key conventions, project structure, and available tools.</returns>
    [McpServerTool(Name = "get_context", ReadOnly = true, Idempotent = true)]
    [ToolAlias("app.context")]
    public partial string GetContext()
    {
        return LoadEmbeddedResource("Celbridge.Tools.Assets.AgentContext.md");
    }

    private static string LoadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(AppTools).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return $"Resource '{resourceName}' not found.";
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
