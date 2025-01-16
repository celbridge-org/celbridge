using Celbridge.Entities;
using Celbridge.Forms;

namespace Celbridge.UserInterface.Models.Forms;

public class TextBlockElement : ITextBlockElement
{
    public string Text { get; set; } = string.Empty;

    public PropertyBinding? TextBinding { get; set; }

    public bool Italic { get; set; }

    public bool Bold { get; set; }  

    public ITextBlockElement WithText(string text)
    {
        Text = text;
        return this;
    }

    public ITextBlockElement WithItalic()
    {
        Italic = true;
        return this;
    }

    public ITextBlockElement WithBold()
    {
        Bold = true;
        return this;
    }

    public ITextBlockElement BindText(string propertyPath, PropertyBindingMode bindingMode)
    {
        TextBinding = new PropertyBinding(propertyPath, bindingMode);
        return this;
    }
}
