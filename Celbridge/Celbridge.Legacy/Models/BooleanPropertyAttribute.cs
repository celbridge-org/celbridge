namespace Celbridge.Legacy.Models;

public class BooleanPropertyAttribute : PropertyAttribute
{
    public BooleanPropertyAttribute()
        : base("Celbridge.Legacy.Views.BooleanPropertyView", typeof(bool))
    {}
}
