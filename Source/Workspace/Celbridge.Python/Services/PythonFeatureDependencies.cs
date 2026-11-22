using Celbridge.Dependencies;
using Celbridge.Settings;

namespace Celbridge.Python.Services;

/// <summary>
/// The dependencies of the python feature: uv, then the celbridge wheel, then basedpyright.
/// </summary>
public sealed class PythonFeatureDependencies : IFeatureDependencies
{
    public PythonFeatureDependencies(
        UvDependency uvDependency,
        CelbridgeWheelDependency celbridgeWheelDependency,
        BasedpyrightDependency basedpyrightDependency)
    {
        // The celbridge wheel and basedpyright install with uv, so uv installs first. The celbridge wheel's install
        // fetches an interpreter, and basedpyright's tool environment runs on the same interpreter.
        Dependencies = new List<IDependency>
        {
            uvDependency,
            celbridgeWheelDependency,
            basedpyrightDependency,
        };
    }

    public FeatureFlagDescriptor Feature => FeatureFlagCatalog.Python;

    public IReadOnlyList<IDependency> Dependencies { get; }
}
