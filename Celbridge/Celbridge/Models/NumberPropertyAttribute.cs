namespace Celbridge.Models
{
    public class NumberPropertyAttribute : PropertyAttribute
    {
        public NumberPropertyAttribute()
            : base("Celbridge.Views.NumberPropertyView", typeof(double))
        {}
    }
}
