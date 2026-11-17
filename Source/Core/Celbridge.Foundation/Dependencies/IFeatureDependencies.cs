using Celbridge.Settings;

namespace Celbridge.Dependencies;

/// <summary>
/// The dependencies of one feature. A module registers one implementation for each feature that has dependencies.
/// </summary>
public interface IFeatureDependencies
{
    /// <summary>
    /// Returns the feature that needs the dependencies.
    /// </summary>
    FeatureFlagDescriptor Feature { get; }

    /// <summary>
    /// Returns the feature's dependencies, in the order they install. A later dependency may need an earlier one.
    /// </summary>
    IReadOnlyList<IDependency> Dependencies { get; }
}
