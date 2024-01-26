namespace CelLegacy.Models;

public class TextAreaPropertyAttribute : PropertyAttribute
{
    public TextAreaPropertyAttribute()
        : base("CelLegacy.Views.TextAreaPropertyView", typeof(string))
    {}
}
