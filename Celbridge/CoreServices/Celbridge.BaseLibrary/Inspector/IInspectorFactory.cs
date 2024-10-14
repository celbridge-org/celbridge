namespace Celbridge.Inspector;

/// <summary>
/// A factory class for instantiating inspector instances.
/// </summary>
public interface IInspectorFactory
{
    /// <summary>
    /// Create an inspector for the specified resource.
    /// </summary>
    IInspector CreateInspector(ResourceKey resource);
}
