namespace Celbridge.Models
{
    public class BooleanPropertyAttribute : PropertyAttribute
    {
        public BooleanPropertyAttribute()
            : base("Celbridge.Views.BooleanPropertyView", typeof(bool))
        {}
    }
}
