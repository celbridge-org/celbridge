using Celbridge.Dependencies;
using Celbridge.Settings;

namespace Celbridge.Python.Services;

/// <summary>
/// The dependencies of the python feature: uv, then the celbridge wheel.
/// </summary>
public sealed class PythonFeatureDependencies : IFeatureDependencies
{
    public PythonFeatureDependencies(UvDependency uvDependency, CelbridgeWheelDependency celbridgeWheelDependency)
    {
        // The celbridge wheel installs with uv, so uv installs first.
        Dependencies = new List<IDependency>
        {
            uvDependency,
            celbridgeWheelDependency,
        };
    }

    public FeatureFlagDescriptor Feature => FeatureFlagCatalog.Python;

    public IReadOnlyList<IDependency> Dependencies { get; }
}
