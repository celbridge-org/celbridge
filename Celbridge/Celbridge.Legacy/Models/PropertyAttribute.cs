namespace Celbridge.Legacy.Models;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = false)]
public abstract class PropertyAttribute : Attribute
{
    public string ViewName { get; }
    public Type PropertyType { get; }

    public PropertyAttribute(string viewName, Type propertyType)
    {
        ViewName = viewName;
        PropertyType = propertyType;
    }
}
