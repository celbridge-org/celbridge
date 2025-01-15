namespace Celbridge.Forms;

/// <summary>
/// A form element that can contain other form elements.
/// </summary>
public interface IFormPanel : IFormElement
{
    /// <summary>
    /// The child elements of this panel.
    /// </summary>
    IList<IFormElement> Children { get; }
}
