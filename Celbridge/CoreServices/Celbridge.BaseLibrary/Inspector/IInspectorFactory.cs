namespace Celbridge.Inspector;

/// <summary>
/// A factory class for instantiating inspector instances to display in the inspector panel.
/// </summary>
public interface IInspectorFactory
{
    /// <summary>
    /// Creates an inspector to display the resource name in the inspector panel.
    /// </summary>
    Result<IInspector> CreateResourceNameInspector(ResourceKey key);

    /// <summary>
    /// Creates an inspector based on the resource type.
    /// </summary>
    Result<IInspector> CreateResourceInspector(ResourceKey resource);
}
