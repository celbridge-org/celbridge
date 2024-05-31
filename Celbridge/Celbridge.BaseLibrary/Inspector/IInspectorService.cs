namespace Celbridge.BaseLibrary.Inspector;

/// <summary>
/// The inspector service provides functionality to support the inspector panel in the workspace UI.
/// </summary>
public interface IInspectorService
{
    /// <summary>
    /// Factory method to create the inspector panel for the workspace UI.
    /// </summary>
    object CreateInspectorPanel();
}
