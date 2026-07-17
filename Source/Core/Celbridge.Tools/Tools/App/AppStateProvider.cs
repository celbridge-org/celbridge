using System.Reflection;
using Celbridge.Messaging;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.Workspace;

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
/// Result returned by app_get_state, reporting the running version, project load
/// state, feature flag states, the focused panel ("None" when unfocused), the utility
/// currently shown in the Utility Panel rail ("" when no project is loaded), whether the
/// platform supplies a native menu bar, the current panel layout, and the spotlightable landmark ids
/// app_spotlight accepts.
/// </summary>
public record class AppStateResult(
    string Version,
    bool IsLoaded,
    string ProjectName,
    IReadOnlyDictionary<string, bool> FeatureFlags,
    string FocusedPanel,
    string ActiveUtility,
    LayoutModeInfo LayoutMode,
    IReadOnlyList<string> SpotlightLandmarks,
    bool UsesNativeMenuBar = false);

/// <summary>
/// Builds the AppStateResult snapshot describing current app and workspace state.
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
    private readonly IPlatformInfo _platformInfo;
    private readonly IProjectService _projectService;
    private readonly IFeatureFlags _featureFlags;
    private readonly IFocusService _focusService;
    private readonly ILayoutService _layoutService;
    private readonly ISpotlightRegistry _spotlightRegistry;

    // The most recently broadcast active Utility Panel surface, cached from ActiveUtilityChangedMessage so
    // app_get_state can report it without reading the UI panel off the tool thread. Reference assignment is
    // atomic, so the cross-thread read needs no lock.
    private string _activeUtilityId = string.Empty;

    public AppStateProvider(
        IAppEnvironment environmentService,
        IPlatformInfo platformInfo,
        IProjectService projectService,
        IFeatureFlags featureFlags,
        IFocusService focusService,
        ILayoutService layoutService,
        ISpotlightRegistry spotlightRegistry,
        IMessengerService messengerService)
    {
        _environmentService = environmentService;
        _platformInfo = platformInfo;
        _projectService = projectService;
        _featureFlags = featureFlags;
        _focusService = focusService;
        _layoutService = layoutService;
        _spotlightRegistry = spotlightRegistry;

        // This provider is a singleton, so the subscription lives for the app lifetime (no unregister needed).
        messengerService.Register<ActiveUtilityChangedMessage>(this, OnActiveUtilityChanged);
    }

    private void OnActiveUtilityChanged(object recipient, ActiveUtilityChangedMessage message)
    {
        _activeUtilityId = message.UtilityId;
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

        var activeUtility = isLoaded ? _activeUtilityId : string.Empty;

        var layoutMode = new LayoutModeInfo(
            ContextPanelVisible: _layoutService.IsContextPanelVisible,
            InspectorPanelVisible: _layoutService.IsInspectorPanelVisible,
            ConsolePanelVisible: _layoutService.IsConsolePanelVisible,
            ConsoleMaximized: _layoutService.IsConsoleMaximized);

        var spotlightLandmarks = _spotlightRegistry.GetLandmarks()
            .Select(landmark => landmark.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return new AppStateResult(
            Version: version,
            IsLoaded: isLoaded,
            ProjectName: projectName,
            FeatureFlags: featureFlags,
            FocusedPanel: focusedPanel,
            ActiveUtility: activeUtility,
            LayoutMode: layoutMode,
            SpotlightLandmarks: spotlightLandmarks,
            UsesNativeMenuBar: _platformInfo.UsesNativeMenuBar);
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
