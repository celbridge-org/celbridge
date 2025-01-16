using Celbridge.Entities;

namespace Celbridge.Forms;

/// <summary>
/// A text block form element.
/// </summary>
public interface ITextBlockElement : IFormElement
{
    /// <summary>
    /// The text to display in the text block.
    /// </summary>
    string Text { get; set; }

    PropertyBinding? TextBinding { get; set; }

    public bool Italic { get; set; }

    public bool Bold { get; set; }

    /// <summary>
    /// Fluent API to set the text to display in the text block.
    /// </summary>
    ITextBlockElement WithText(string comment);

    ITextBlockElement WithItalic();

    ITextBlockElement WithBold();

    ITextBlockElement BindText(string propertyPath, PropertyBindingMode bindingMode = PropertyBindingMode.OneWay);
}
