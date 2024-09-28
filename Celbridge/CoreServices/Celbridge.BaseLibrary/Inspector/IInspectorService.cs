namespace Celbridge.Inspector;

/// <summary>
/// The inspector service provides functionality to support the inspector panel in the workspace UI.
/// </summary>
public interface IInspectorService
{
    /// <summary>
    /// Returns the inspector panel view.
    /// </summary>
    public IInspectorPanel InspectorPanel { get; }
}
