namespace Celbridge.Settings;

/// <summary>
/// Service for checking if features are enabled via configuration.
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>
    /// Returns true if the specified feature is enabled.
    /// Feature flags are configured in appsettings.json under the "FeatureFlags" section.
    /// For workspace-aware feature checking, use IWorkspaceFeatures instead.
    /// </summary>
    bool IsEnabled(string featureName);
}
