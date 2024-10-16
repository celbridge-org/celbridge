namespace Celbridge.Inspector;

/// <summary>
/// A UI element for viewing and editing the properties of a resource.
/// </summary>
public interface IInspector
{
    /// <summary>
    /// The resource to inspect.
    /// </summary>
    public ResourceKey Resource { set; get; }
}
