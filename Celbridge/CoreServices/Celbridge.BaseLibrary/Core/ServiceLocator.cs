namespace Celbridge.Core;

/// <summary>
/// Provides general access to the dependency injection framework.
/// Use this in situations where you need to acquire a dependency but can't use constructor injection.
/// </summary>
public static class ServiceLocator
{
    private static IServiceProvider? _serviceProvider;

    public static void Initialize(IServiceProvider serviceProvider)
    {
        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        _serviceProvider = serviceProvider;
    }

    public static IServiceProvider ServiceProvider => _serviceProvider!;
}
