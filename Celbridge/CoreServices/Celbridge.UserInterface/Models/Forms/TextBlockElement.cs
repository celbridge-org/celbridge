using Celbridge.Forms;

namespace Celbridge.UserInterface.Models.Forms;

public class TextBlockElement : ITextBlockElement
{
    public string Text { get; set; } = string.Empty;

    public ITextBlockElement WithText(string text)
    {
        Text = text;
        return this;
    }
}
