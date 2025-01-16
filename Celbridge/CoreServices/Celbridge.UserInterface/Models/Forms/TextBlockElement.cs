using Celbridge.Entities;
using Celbridge.Forms;

namespace Celbridge.UserInterface.Models.Forms;

public class TextBlockElement : ITextBlockElement
{
    public string Text { get; set; } = string.Empty;

    public PropertyBinding? TextBinding { get; set; }

    public ITextBlockElement WithText(string text)
    {
        Text = text;
        return this;
    }

    public ITextBlockElement BindText(ComponentKey componentKey, string propertyPath, PropertyBindingMode bindingMode)
    {
        TextBinding = new PropertyBinding(componentKey, propertyPath, bindingMode);
        return this;
    }
}
