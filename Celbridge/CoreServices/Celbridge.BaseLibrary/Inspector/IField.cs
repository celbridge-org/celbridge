namespace Celbridge.Inspector;

/// <summary>
/// A field UI element for editing a property.
/// </summary>
public interface IField
{
    /// <summary>
    /// The UI element used to edit the field.
    /// </summary>
    public object UIElement { get; }
}
