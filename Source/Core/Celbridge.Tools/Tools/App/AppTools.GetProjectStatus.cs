using System.Text.Json;
using Celbridge.Projects;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by app_get_status with the project load state.
/// </summary>
public record class ProjectStatusResult(bool IsLoaded, string ProjectName);

public partial class AppTools
{
    /// <summary>
    /// Returns the project status as JSON with isLoaded and projectName fields.
    /// </summary>
    /// <returns>JSON object with fields: isLoaded (bool), projectName (string).</returns>
    [McpServerTool(Name = "app_get_status", ReadOnly = true, Idempotent = true)]
    [ToolAlias("app.get_status")]
    public partial CallToolResult GetProjectStatus()
    {
        var projectService = GetRequiredService<IProjectService>();
        var currentProject = projectService.CurrentProject;

        var isLoaded = currentProject is not null;
        var projectName = currentProject?.ProjectName ?? "";

        var result = new ProjectStatusResult(isLoaded, projectName);
        var json = JsonSerializer.Serialize(result, JsonOptions);

        return SuccessResult(json);
    }
}
