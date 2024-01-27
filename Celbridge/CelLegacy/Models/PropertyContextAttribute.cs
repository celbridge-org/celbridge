namespace Celbridge.Legacy.Models;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class PropertyContextAttribute : Attribute
{
    public PropertyContext Context { get; private set; }

    public PropertyContextAttribute(PropertyContext context) 
    { 
        Context = context;
    }
}
