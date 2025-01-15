using Celbridge.Entities;
using Celbridge.Forms;

namespace Celbridge.UserInterface.Models.Forms;

public class TextBlockElement : ITextBlockElement
{
    public string Text { get; set; } = string.Empty;

    public ComponentKey ComponentKey { get; set; }
    public string PropertyPath { get; set; } = string.Empty;

    public ITextBlockElement WithText(string text)
    {
        Text = text;
        return this;
    }

    public ITextBlockElement BindText(ComponentKey componentKey, string propertyPath)
    {
        ComponentKey = componentKey;
        PropertyPath = propertyPath;

        return this;
    }
}
