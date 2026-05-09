using System.Reflection;
using System.Text.Json;
using Celbridge.ApplicationEnvironment;
using Celbridge.Projects;
using Celbridge.Settings;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Workspace layout snapshot reported as part of app_get_state. Reflects which
/// regions are currently visible and whether the console is maximised.
/// </summary>
public record class LayoutModeInfo(
    bool ContextPanelVisible,
    bool InspectorPanelVisible,
    bool ConsolePanelVisible,
    bool ConsoleMaximized);

/// <summary>
/// Result returned by app_get_state. version is the running Celbridge version.
/// featureFlags maps each public flag name declared in FeatureFlagConstants to
/// its current enabled state. focusedPanel is the WorkspacePanel currently
/// holding focus (or "None"). layoutMode reports current panel visibility.
/// </summary>
public record class AppStateResult(
    string Version,
    bool IsLoaded,
    string ProjectName,
    IReadOnlyDictionary<string, bool> FeatureFlags,
    string FocusedPanel,
    LayoutModeInfo LayoutMode);

public partial class AppTools
{
    // Cached set of public flag names declared on FeatureFlagConstants. Reading
    // them via reflection means adding a new constant automatically widens the
    // get_state payload.
    private static readonly IReadOnlyList<string> KnownFeatureFlagNames = ReadFeatureFlagNames();

    /// <summary>App state: app version, project load status, feature flags, focused panel, layout.</summary>
    [McpServerTool(Name = "app_get_state", ReadOnly = true, Idempotent = true)]
    [ToolAlias("app.get_state")]
    [RelatedGuides("workspace_panels", "project_structure")]
    public partial CallToolResult GetState()
    {
        var environmentService = GetRequiredService<IEnvironmentService>();
        var version = environmentService.GetEnvironmentInfo().AppVersion;

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

        var panelFocusService = GetRequiredService<IPanelFocusService>();
        var focusedPanel = panelFocusService.FocusedPanel.ToString();

        var layoutService = GetRequiredService<ILayoutService>();
        var layoutMode = new LayoutModeInfo(
            ContextPanelVisible: layoutService.IsContextPanelVisible,
            InspectorPanelVisible: layoutService.IsInspectorPanelVisible,
            ConsolePanelVisible: layoutService.IsConsolePanelVisible,
            ConsoleMaximized: layoutService.IsConsoleMaximized);

        var result = new AppStateResult(
            Version: version,
            IsLoaded: isLoaded,
            ProjectName: projectName,
            FeatureFlags: featureFlags,
            FocusedPanel: focusedPanel,
            LayoutMode: layoutMode);

        var json = JsonSerializer.Serialize(result, JsonOptions);

        return ToolResponse.Success(json);
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
