using System.Reflection;
using System.Text.Json;
using Celbridge.Projects;
using Celbridge.Python;
using Celbridge.Settings;
using Celbridge.Workspace;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Pointer to the agent guide library carried in the app_get_state payload so
/// that compliant agents discover the guide surface on their mandatory first
/// state call instead of relying on tool-name pattern matching.
/// </summary>
public record class AgentDocsPointer(string Entry, string Via);

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
/// Python environment snapshot reported as part of app_get_state. The package
/// list is reported by the Python host at startup and remains stable for the
/// life of the process.
/// </summary>
public record class PythonEnvironmentSnapshot(IReadOnlyList<string> InstalledPackages);

/// <summary>
/// Result returned by app_get_state. featureFlags maps each public flag name
/// declared in FeatureFlagConstants to its current enabled state. agentDocs
/// names the orientation entry point and the tool to read it through.
/// focusedPanel is the WorkspacePanel currently holding focus (or "None").
/// layoutMode reports current panel visibility. pythonEnvironment carries the
/// installed package list reported by the Python host.
/// </summary>
public record class AppStateResult(
    bool IsLoaded,
    string ProjectName,
    IReadOnlyDictionary<string, bool> FeatureFlags,
    AgentDocsPointer AgentDocs,
    string FocusedPanel,
    LayoutModeInfo LayoutMode,
    PythonEnvironmentSnapshot PythonEnvironment);

public partial class AppTools
{
    // Cached set of public flag names declared on FeatureFlagConstants. Reading
    // them via reflection means adding a new constant automatically widens the
    // get_state payload.
    private static readonly IReadOnlyList<string> KnownFeatureFlagNames = ReadFeatureFlagNames();

    // Static pointer to the orientation guide.
    private static readonly AgentDocsPointer AgentDocsPointerValue = new("getting_started", "guides_read");

    // Bootstrap tool. Keep summary rich and do not trim.
    /// <summary>
    /// Returns application-level state as JSON. Covers project load status,
    /// the featureFlags map agents must consult before invoking a
    /// feature-gated tool (e.g. webview-dev-tools, webview-dev-tools-eval),
    /// the agentDocs pointer naming the orientation entry point in the agent
    /// guide library, the currently focused workspace panel, the visibility
    /// of each layout region, and the installed Python package list reported
    /// by the Python host.
    /// </summary>
    /// <returns>JSON object with fields: isLoaded (bool), projectName (string), featureFlags (object mapping flag name to bool), agentDocs (object with entry and via fields), focusedPanel (string WorkspacePanel name), layoutMode (object describing region visibility), pythonEnvironment (object with installedPackages array).</returns>
    [McpServerTool(Name = "app_get_state", ReadOnly = true, Idempotent = true)]
    [ToolAlias("app.get_state")]
    public partial CallToolResult GetState()
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

        var panelFocusService = GetRequiredService<IPanelFocusService>();
        var focusedPanel = panelFocusService.FocusedPanel.ToString();

        var layoutService = GetRequiredService<ILayoutService>();
        var layoutMode = new LayoutModeInfo(
            ContextPanelVisible: layoutService.IsContextPanelVisible,
            InspectorPanelVisible: layoutService.IsInspectorPanelVisible,
            ConsolePanelVisible: layoutService.IsConsolePanelVisible,
            ConsoleMaximized: layoutService.IsConsoleMaximized);

        var pythonEnvironment = new PythonEnvironmentSnapshot(PythonEnvironmentInfo.InstalledPackages);

        var result = new AppStateResult(
            IsLoaded: isLoaded,
            ProjectName: projectName,
            FeatureFlags: featureFlags,
            AgentDocs: AgentDocsPointerValue,
            FocusedPanel: focusedPanel,
            LayoutMode: layoutMode,
            PythonEnvironment: pythonEnvironment);

        var json = JsonSerializer.Serialize(result, JsonOptions);

        return ToolSuccess(json);
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
