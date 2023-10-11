namespace Celbridge.Models
{
    public class PathPropertyAttribute : PropertyAttribute
    {
        public PathPropertyAttribute()
            : base("Celbridge.Views.PathPropertyView", typeof(string))
        {}
    }
}
