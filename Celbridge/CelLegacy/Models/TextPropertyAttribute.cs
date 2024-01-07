namespace Celbridge.Models
{
    public class TextPropertyAttribute : PropertyAttribute
    {
        public TextPropertyAttribute()
            : base("Celbridge.Views.TextPropertyView", typeof(string))
        {}
    }
}
