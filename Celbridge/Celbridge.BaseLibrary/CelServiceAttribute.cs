namespace Celbridge.BaseLibrary;

// Enum to define service lifetime options
public enum CelServiceLifetime
{
    Singleton,
    Scoped,
    Transient
}

// Attribute to mark services for automatic DI registration
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CelServiceAttribute : Attribute
{
    public CelServiceLifetime Lifetime { get; }

    public CelServiceAttribute(CelServiceLifetime lifetime)
    {
        Lifetime = lifetime;
    }
}
