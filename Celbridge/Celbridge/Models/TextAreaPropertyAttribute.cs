namespace Celbridge.Models
{
    public class TextAreaPropertyAttribute : PropertyAttribute
    {
        public TextAreaPropertyAttribute()
            : base("Celbridge.Views.TextAreaPropertyView", typeof(string))
        {}
    }
}
