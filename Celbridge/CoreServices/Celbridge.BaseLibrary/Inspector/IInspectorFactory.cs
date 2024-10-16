namespace Celbridge.Inspector;

/// <summary>
/// A factory class for instantiating inspector instances.
/// </summary>
public interface IInspectorFactory
{
    /// <summary>
    /// Creates an inspector that can inspect a resource.
    /// This is the inspector displayed at the top of the inspector panel for any selected resource.
    /// </summary>
    Result<IInspector> CreateResourceNameInspector(ResourceKey key);
}
