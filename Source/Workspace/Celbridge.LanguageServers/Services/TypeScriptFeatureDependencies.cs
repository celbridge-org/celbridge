using Celbridge.Dependencies;
using Celbridge.Settings;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// The dependencies of the typescript feature.
/// </summary>
public sealed class TypeScriptFeatureDependencies : IFeatureDependencies
{
    public TypeScriptFeatureDependencies(TypeScriptDependency typeScriptDependency)
    {
        Dependencies = new List<IDependency>
        {
            typeScriptDependency,
        };
    }

    public FeatureFlagDescriptor Feature => FeatureFlagCatalog.TypeScript;

    public IReadOnlyList<IDependency> Dependencies { get; }
}
