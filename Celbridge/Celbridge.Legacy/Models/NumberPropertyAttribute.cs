namespace Celbridge.Legacy.Models;

public class NumberPropertyAttribute : PropertyAttribute
{
    public NumberPropertyAttribute()
        : base("Celbridge.Legacy.Views.NumberPropertyView", typeof(double))
    {}
}
