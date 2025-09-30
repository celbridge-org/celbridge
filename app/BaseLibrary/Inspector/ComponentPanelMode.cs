namespace Celbridge.Inspector;

/// <summary>
/// Current editing mode for the component panel in the inspector.
/// </summary>
public enum ComponentPanelMode
{
    /// <summary>
    /// No component is being edited.
    /// </summary>
    None,

    /// <summary>
    /// Component properties are being edited.
    /// </summary>
    ComponentValue,

    /// <summary>
    /// Component type is being edited.
    /// </summary>
    ComponentType
}
