namespace Celbridge.Legacy.Models;

public class TextAreaPropertyAttribute : PropertyAttribute
{
    public TextAreaPropertyAttribute()
        : base("Celbridge.Legacy.Views.TextAreaPropertyView", typeof(string))
    {}
}
