namespace CelLegacy.Models;

public class BooleanPropertyAttribute : PropertyAttribute
{
    public BooleanPropertyAttribute()
        : base("CelLegacy.Views.BooleanPropertyView", typeof(bool))
    {}
}
