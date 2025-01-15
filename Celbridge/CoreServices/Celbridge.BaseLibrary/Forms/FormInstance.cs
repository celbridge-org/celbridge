namespace Celbridge.Forms;

/// <summary>
/// An instance of a form, with a reference to the form definition and the UI element constructed from it.
/// </summary>
public record FormInstance(IForm Form, object UIElement);
