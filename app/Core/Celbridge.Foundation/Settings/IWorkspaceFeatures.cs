namespace Celbridge.Settings;

/// <summary>
/// Service for checking if features are enabled at the workspace level.
/// </summary>
public interface IWorkspaceFeatures
{
    /// <summary>
    /// Returns true if the specified feature is enabled.
    /// Checks both workspace-specific features (from .celbridge file) and application-level features (from appsettings.json).
    /// </summary>
    bool IsEnabled(string featureName);
}
