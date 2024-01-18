namespace CelLegacy.Models;

public class TextPropertyAttribute : PropertyAttribute
{
    public TextPropertyAttribute()
        : base("CelLegacy.Views.TextPropertyView", typeof(string))
    {}
}
