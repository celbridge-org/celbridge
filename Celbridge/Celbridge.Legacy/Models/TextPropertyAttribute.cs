namespace Celbridge.Legacy.Models;

public class TextPropertyAttribute : PropertyAttribute
{
    public TextPropertyAttribute()
        : base("Celbridge.Legacy.Views.TextPropertyView", typeof(string))
    {}
}
