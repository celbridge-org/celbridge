namespace Celbridge.Settings;

/// <summary>
/// Service for checking if features are enabled via configuration.
/// Supports project-level overrides that take precedence over application-level settings.
/// </summary>
public interface IFeatureFlags
{
    /// <summary>
    /// Returns true if the specified feature is enabled.
    /// Checks project overrides first, then falls back to application-level configuration.
    /// </summary>
    bool IsEnabled(string featureName);

    /// <summary>
    /// Returns the application-level value for a feature (appsettings.json, or the enabled default when
    /// unset), ignoring any project override. Used to show what a project inheriting the default resolves to.
    /// </summary>
    bool GetApplicationValue(string featureName);

    /// <summary>
    /// Applies project-level feature flag overrides.
    /// These take precedence over application-level settings.
    /// </summary>
    void ApplyProjectOverrides(IReadOnlyDictionary<string, bool> overrides);

    /// <summary>
    /// Clears all project-level overrides, reverting to application-level settings only.
    /// </summary>
    void ClearProjectOverrides();
}
