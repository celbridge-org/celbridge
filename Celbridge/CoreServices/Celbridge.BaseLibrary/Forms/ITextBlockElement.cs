namespace Celbridge.Forms;

/// <summary>
/// A text block form element.
/// </summary>
public interface ITextBlockElement : IFormElement
{
    /// <summary>
    /// The text to display in the text block.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Fluent API to set the text to display in the text block.
    /// </summary>
    ITextBlockElement WithText(string comment);
}
