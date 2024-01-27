namespace Celbridge.Legacy.Models;

public class PathPropertyAttribute : PropertyAttribute
{
    public PathPropertyAttribute()
        : base("Celbridge.Legacy.Views.PathPropertyView", typeof(string))
    {}
}
