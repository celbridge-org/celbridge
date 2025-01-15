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

    ComponentKey ComponentKey { get; set; }
    string PropertyPath { get; set; }

    /// <summary>
    /// Fluent API to set the text to display in the text block.
    /// </summary>
    ITextBlockElement WithText(string comment);

    ITextBlockElement BindText(ComponentKey componentKey, string propertyPath);
}
