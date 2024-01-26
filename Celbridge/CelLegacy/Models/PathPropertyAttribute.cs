namespace CelLegacy.Models;

public class PathPropertyAttribute : PropertyAttribute
{
    public PathPropertyAttribute()
        : base("CelLegacy.Views.PathPropertyView", typeof(string))
    {}
}
