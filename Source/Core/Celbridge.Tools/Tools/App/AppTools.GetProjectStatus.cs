using System.Reflection;
using System.Text.Json;
using Celbridge.Projects;
using Celbridge.Settings;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by app_get_status. featureFlags maps each public flag name
/// declared in FeatureFlagConstants to its current enabled state.
/// </summary>
public record class ProjectStatusResult(bool IsLoaded, string ProjectName, IReadOnlyDictionary<string, bool> FeatureFlags);

public partial class AppTools
{
    // Cached set of public flag names declared on FeatureFlagConstants. Reading
    // them via reflection means adding a new constant automatically widens the
    // get_status payload — no second site to update.
    private static readonly IReadOnlyList<string> KnownFeatureFlagNames = ReadFeatureFlagNames();

    /// <summary>
    /// Returns the project status as JSON with isLoaded, projectName, and a
    /// featureFlags map. The featureFlags map covers every flag the host knows
    /// about (e.g. webview-dev-tools, webview-dev-tools-eval) and lets agents
    /// decide whether to attempt a feature-gated tool before calling it.
    /// </summary>
    /// <returns>JSON object with fields: isLoaded (bool), projectName (string), featureFlags (object mapping flag name to bool).</returns>
    [McpServerTool(Name = "app_get_status", ReadOnly = true, Idempotent = true)]
    [ToolAlias("app.get_status")]
    public partial CallToolResult GetProjectStatus()
    {
        var projectService = GetRequiredService<IProjectService>();
        var currentProject = projectService.CurrentProject;

        var isLoaded = currentProject is not null;
        var projectName = currentProject?.ProjectName ?? "";

        var featureFlagsService = GetRequiredService<IFeatureFlags>();
        var featureFlags = new Dictionary<string, bool>(KnownFeatureFlagNames.Count);
        foreach (var flagName in KnownFeatureFlagNames)
        {
            featureFlags[flagName] = featureFlagsService.IsEnabled(flagName);
        }

        var result = new ProjectStatusResult(isLoaded, projectName, featureFlags);
        var json = JsonSerializer.Serialize(result, JsonOptions);

        return SuccessResult(json);
    }

    private static IReadOnlyList<string> ReadFeatureFlagNames()
    {
        var fields = typeof(FeatureFlagConstants).GetFields(BindingFlags.Public | BindingFlags.Static);
        var names = new List<string>(fields.Length);
        foreach (var field in fields)
        {
            if (field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            {
                var value = (string?)field.GetRawConstantValue();
                if (!string.IsNullOrEmpty(value))
                {
                    names.Add(value);
                }
            }
        }
        names.Sort(StringComparer.Ordinal);
        return names;
    }
}
