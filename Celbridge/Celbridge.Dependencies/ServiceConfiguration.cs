namespace Celbridge.BaseLibrary.Extensions;

/// <summary>
/// Helper class to allow Celbridge extensions to register types for use with Dependency Injection without
/// needing a dependency on Microsoft.Extensions.DependencyInjection.
/// </summary>
public class ServiceConfiguration : IServiceConfiguration
{
    private List<Type> TransientServices { get; } = new();
    private Dictionary<Type, Type> TransientInterfaceServices { get; } = new();
    private List<Type> SingletonServices { get; } = new();
    private Dictionary<Type, Type> SingletonInterfaceServices { get; } = new();

    public void AddTransient<T>() 
        where T : class
    {
        TransientServices.Add(typeof(T));
    }

    public void AddTransient<I, T>() 
        where I : class
        where T : class
    {
        TransientInterfaceServices.Add(typeof(I), typeof(T));
    }

    public void AddSingleton<T>()
    where T : class
    {
        SingletonServices.Add(typeof(T));
    }

    public void AddSingleton<I, T>()
    where I : class
    where T : class
    {
        SingletonInterfaceServices.Add(typeof(I), typeof(T));
    }

    public void ConfigureServices(IServiceCollection services)
    {
        foreach (var serviceType in TransientServices)
        {
            services.AddTransient(serviceType);
        }

        foreach (var kv in TransientInterfaceServices)
        {
            services.AddTransient(kv.Key, kv.Value);
        }

        foreach (var serviceType in SingletonServices)
        {
            services.AddSingleton(serviceType);
        }

        foreach (var kv in SingletonInterfaceServices)
        {
            services.AddSingleton(kv.Key, kv.Value);
        }
    }
}
