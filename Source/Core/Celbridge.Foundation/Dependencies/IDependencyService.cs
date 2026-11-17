using Celbridge.Settings;

namespace Celbridge.Dependencies;

/// <summary>
/// A dependency that failed to install, with the feature that needs it and the error.
/// </summary>
public record DependencyInstallFailure(FeatureFlagDescriptor Feature, string DependencyName, Result Error);

/// <summary>
/// Installs the dependencies of a project's features.
/// </summary>
public interface IDependencyService
{
    /// <summary>
    /// Installs each missing dependency of the features that are on, in each feature's order, and stops at the first
    /// failure. A project's value for a feature replaces the application's default. Returns the failure, or null once
    /// every dependency is in place.
    /// </summary>
    Task<DependencyInstallFailure?> InstallDependenciesAsync(IReadOnlyDictionary<string, bool> projectFeatures);
}
