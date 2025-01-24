namespace Celbridge.Core;

/// <summary>
/// Provides general access to the dependency injection framework.
/// Use this in situations where you need to acquire a dependency but can't use constructor injection.
/// </summary>
public static class ServiceLocator
{
    private static IServiceProvider? _serviceProvider;

    /// <summary>
    /// Initializes the service locator with the service provider.
    /// </summary>
    public static void Initialize(IServiceProvider serviceProvider)
    {
        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Extension method to acquire a service from the service provider.
    /// This can be used in contexts where Microsoft.Extensions.DependencyInjection should be avoided, e.g. Modules.
    /// </summary>
    public static T AcquireService<T>(this IServiceProvider provider) where T : class
    {
        var service = provider.GetService(typeof(T)) as T;
        Guard.IsNotNull(service, $"Failed to acquire unknown service: '{typeof(T)}'");
        return service;
    }

    /// <summary>
    /// Service locator method that can be used to acquire services from the service provider from anywhere.
    /// This is a bit of an anti-pattern, so should only be used where regualar dependency injection is not viable (e.g. constuction XAML views)
    /// </summary>
    public static T AcquireService<T>() where T : class
    {
        Guard.IsNotNull(_serviceProvider, "ServiceLocator not initialized");
        return _serviceProvider.AcquireService<T>();
    }
}
