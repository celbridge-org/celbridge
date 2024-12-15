namespace Celbridge.Inspector.Models;

public class Field : IField
{
    public object UIElement { get; }

    public Field(object formUIElement)
    {
        UIElement = formUIElement;
    }
}
