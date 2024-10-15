namespace Celbridge.Inspector;

/// <summary>
/// A factory class for instantiating inspector instances.
/// </summary>
public interface IInspectorFactory
{
    /// <summary>
    /// Creates a generic inspector that can inspect any type of resource.
    /// This is the inspector displayed at the top of the inspector panel for all resources.
    /// </summary>
    Result<IInspector> CreateGenericInspector(ResourceKey key);

    /// <summary>
    /// Creates a specialized inspector for the specified resource.
    /// Fails if there is no specialized inspector available for this type of resource.
    /// </summary>
    Result<IInspector> CreateSpecializedInspector(ResourceKey resource);
}
