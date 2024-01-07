namespace CelLegacy.Models;

public class NumberPropertyAttribute : PropertyAttribute
{
    public NumberPropertyAttribute()
        : base("CelLegacy.Views.NumberPropertyView", typeof(double))
    {}
}
