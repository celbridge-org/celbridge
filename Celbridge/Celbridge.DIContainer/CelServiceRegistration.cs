using System.Reflection;
using Celbridge.CommonServices;
using Celbridge.CoreExtensions;

namespace Celbridge.DIContainer;

public static class CelServiceRegistration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        //services.AddAutoRegisteredServices(loadedAssemblies);

        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<IConsoleService, ConsoleService>();

        // Other service registrations...
    }

    private static IServiceCollection AddAutoRegisteredServices(this IServiceCollection services, Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var serviceTypes = assembly.GetTypes()
                .Where(type => type.GetCustomAttributes<CelServiceAttribute>(inherit: false).Any())
                .ToList();

            foreach (var type in serviceTypes)
            {
                var attribute = type.GetCustomAttribute<CelServiceAttribute>();
                var interfaces = type.GetInterfaces();

                // Assuming the first interface is the service contract
                // More sophisticated logic might be needed for multiple interfaces
                var serviceInterface = interfaces.FirstOrDefault();

                if (serviceInterface != null)
                {
                    RegisterService(services, serviceInterface, type, attribute!.Lifetime);
                }
            }
        }

        return services;
    }

    private static void RegisterService(IServiceCollection services, Type serviceType, Type implementationType, CelServiceLifetime lifetime)
    {
        switch (lifetime)
        {
            case CelServiceLifetime.Singleton:
                services.AddSingleton(serviceType, implementationType);
                break;
            case CelServiceLifetime.Scoped:
                services.AddScoped(serviceType, implementationType);
                break;
            case CelServiceLifetime.Transient:
                services.AddTransient(serviceType, implementationType);
                break;
            default:
                throw new InvalidOperationException();
        }
    }
}
