namespace Celbridge.Settings;

/// <summary>
/// Service for checking if features are enabled via configuration.
/// Supports project-level overrides that take precedence over application-level settings.
/// </summary>
public interface IFeatureFlags
{
    /// <summary>
    /// Returns true if the flag is on. The applied project overrides come first, then the application's value.
    /// </summary>
    bool IsEnabled(FeatureFlagDescriptor flag);

    /// <summary>
    /// Returns true if the flag is on for a project with the given overrides, by the same rule as IsEnabled. The
    /// applied overrides and TurnOffAllFeatures play no part, so a project's values resolve before the workspace
    /// applies them.
    /// </summary>
    bool IsEnabledForProject(FeatureFlagDescriptor flag, IReadOnlyDictionary<string, bool> projectOverrides);

    /// <summary>
    /// Returns the application-level value for a flag (appsettings.json, or the default when unset),
    /// ignoring any project override. Used to show what a project inheriting the default resolves to.
    /// A flag that is unset, or set to a value that is not a boolean, is off.
    /// </summary>
    bool GetApplicationValue(FeatureFlagDescriptor flag);

    /// <summary>
    /// Applies project-level feature flag overrides, keyed by flag name.
    /// These take precedence over application-level settings.
    /// </summary>
    void ApplyProjectOverrides(IReadOnlyDictionary<string, bool> overrides);

    /// <summary>
    /// Turns every feature off, including the features that the project or the application turns on. The
    /// features stay off until the project overrides are cleared.
    /// </summary>
    void TurnOffAllFeatures();

    /// <summary>
    /// Clears all project-level overrides, reverting to application-level settings only.
    /// </summary>
    void ClearProjectOverrides();
}
