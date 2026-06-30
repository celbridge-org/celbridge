using System.Reflection;
using Celbridge.ApplicationEnvironment;
using Celbridge.Projects;
using Celbridge.Settings;

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

/// <summary>
/// Builds the AppStateResult snapshot consumed by both the app_get_state
/// tool and AgentResponseFilter's session-start auto-attach.
/// </summary>
public interface IAppStateProvider
{
    AppStateResult GetState();
}

internal sealed class AppStateProvider : IAppStateProvider
{
    // Cached set of public flag names declared on FeatureFlagConstants. Reading
    // them via reflection means adding a new constant automatically widens the
    // get_state payload.
    private static readonly IReadOnlyList<string> KnownFeatureFlagNames = ReadFeatureFlagNames();

    private readonly IAppEnvironment _environmentService;
    private readonly IProjectService _projectService;
    private readonly IFeatureFlags _featureFlags;
    private readonly IFocusService _focusService;
    private readonly ILayoutService _layoutService;

    public AppStateProvider(
        IAppEnvironment environmentService,
        IProjectService projectService,
        IFeatureFlags featureFlags,
        IFocusService focusService,
        ILayoutService layoutService)
    {
        _environmentService = environmentService;
        _projectService = projectService;
        _featureFlags = featureFlags;
        _focusService = focusService;
        _layoutService = layoutService;
    }

    public AppStateResult GetState()
    {
        var version = _environmentService.GetEnvironmentInfo().AppVersion;

        var currentProject = _projectService.CurrentProject;
        var isLoaded = currentProject is not null;
        var projectName = currentProject?.ProjectName ?? "";

        var featureFlags = new Dictionary<string, bool>(KnownFeatureFlagNames.Count);
        foreach (var flagName in KnownFeatureFlagNames)
        {
            featureFlags[flagName] = _featureFlags.IsEnabled(flagName);
        }

        var focusedPanel = _focusService.FocusedPanel.ToString();

        var layoutMode = new LayoutModeInfo(
            ContextPanelVisible: _layoutService.IsContextPanelVisible,
            InspectorPanelVisible: _layoutService.IsInspectorPanelVisible,
            ConsolePanelVisible: _layoutService.IsConsolePanelVisible,
            ConsoleMaximized: _layoutService.IsConsoleMaximized);

        return new AppStateResult(
            Version: version,
            IsLoaded: isLoaded,
            ProjectName: projectName,
            FeatureFlags: featureFlags,
            FocusedPanel: focusedPanel,
            LayoutMode: layoutMode);
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
