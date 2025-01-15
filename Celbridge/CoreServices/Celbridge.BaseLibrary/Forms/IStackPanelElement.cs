namespace Celbridge.Forms;

/// <summary>
/// A stack panel form element.
/// </summary>
public interface IStackPanelElement : IFormPanel
{
    /// <summary>
    /// The orientation of the stack panel.
    /// </summary>
    FormOrientation Orientation { get; set; }

    /// <summary>
    /// Fluent API for adding children to the stack panel.
    /// </summary>
    IStackPanelElement AddChildren(params IFormElement[] childElements);
}
