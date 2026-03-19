using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Modules.Services;

/// <summary>
/// Helper class to allow Celbridge extensions to register types for use with dependency injection without
/// using the Microsoft.Extensions.DependencyInjection package.
/// </summary>
public class ModuleServiceCollection : IModuleServiceCollection
{
    private List<Type> TransientServices { get; } = new();
    private List<(Type Interface, Type Implementation)> TransientInterfaceServices { get; } = new();
    private List<Type> SingletonServices { get; } = new();
    private List<(Type Interface, Type Implementation)> SingletonInterfaceServices { get; } = new();

    public void AddTransient<T>()
        where T : class
    {
        TransientServices.Add(typeof(T));
    }

    public void AddTransient<I, T>()
        where I : class
        where T : class
    {
        TransientInterfaceServices.Add((typeof(I), typeof(T)));
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
        SingletonInterfaceServices.Add((typeof(I), typeof(T)));
    }

    public void PopulateServices(IServiceCollection services)
    {
        foreach (var serviceType in TransientServices)
        {
            services.AddTransient(serviceType);
        }

        foreach (var (interfaceType, implementationType) in TransientInterfaceServices)
        {
            services.AddTransient(interfaceType, implementationType);
        }

        foreach (var serviceType in SingletonServices)
        {
            services.AddSingleton(serviceType);
        }

        foreach (var (interfaceType, implementationType) in SingletonInterfaceServices)
        {
            services.AddSingleton(interfaceType, implementationType);
        }
    }
}
