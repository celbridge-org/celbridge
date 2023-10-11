namespace Celbridge.Models
{
    public class ExpressionPropertyAttribute : PropertyAttribute
    {
        public ExpressionPropertyAttribute()
            : base("Celbridge.Views.ExpressionPropertyView", typeof(ExpressionBase))
        {}
    }
}
