namespace Celbridge.Settings;

/// <summary>
/// Service for checking if features are enabled via configuration.
/// Supports project-level overrides that take precedence over application-level settings, except for
/// the flags named in FeatureFlagConstants.NonOverridableFlags.
/// </summary>
public interface IFeatureFlags
{
    /// <summary>
    /// Returns true if the specified feature is enabled.
    /// Checks project overrides first, then falls back to application-level configuration. A
    /// non-overridable flag skips the override and reads the application-level value.
    /// </summary>
    bool IsEnabled(string featureName);

    /// <summary>
    /// Returns the application-level value for a feature (appsettings.json, or the default when unset),
    /// ignoring any project override. Used to show what a project inheriting the default resolves to.
    /// An unset feature defaults to enabled, and an unset non-overridable flag to disabled.
    /// </summary>
    bool GetApplicationValue(string featureName);

    /// <summary>
    /// Applies project-level feature flag overrides.
    /// These take precedence over application-level settings. An override naming a non-overridable flag
    /// has no effect.
    /// </summary>
    void ApplyProjectOverrides(IReadOnlyDictionary<string, bool> overrides);

    /// <summary>
    /// Clears all project-level overrides, reverting to application-level settings only.
    /// </summary>
    void ClearProjectOverrides();
}
