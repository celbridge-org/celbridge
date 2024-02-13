using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Settings;
using Celbridge.CommonServices.Logging;
using Celbridge.CommonServices.Messaging;
using Celbridge.CommonServices.Settings;
using System.Reflection;

namespace Celbridge.Dependencies;

/// <summary>
/// Configures the dependency injection framework to support all required services.
/// </summary>
public class ServiceConfiguration
{
    public static void Configure(IServiceCollection services, List<Assembly> extensionAssemblies)
    {
        ConfigureCommonServices(services);
        ConfigureExtensionServices(services, extensionAssemblies);
    }

    private static void ConfigureCommonServices(IServiceCollection services)
    {
        // This assembly has direct references to these common services, so we simply add them
        // to the services collection.

        services.AddTransient<ISettingsContainer, SettingsContainer>();
        services.AddSingleton<IEditorSettings, EditorSettings>();
        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<ILoggingService, LoggingService>();
    }

    private static void ConfigureExtensionServices(IServiceCollection services, List<Assembly> extensionAssemblies)
    {
        // Extension assemblies are loaded by the application and passed in here where we use reflection to
        // register the necessary services for dependency injection.

        var extensionServices = new ExtensionServiceCollection();

        foreach (var assembly in extensionAssemblies)
        {
            // Find all types that implement the IExtension interface
            var extensionTypes = assembly.GetTypes()
                .Where(t => typeof(IExtension).IsAssignableFrom(t) && 
                       !t.IsInterface && 
                       !t.IsAbstract);

            if (extensionTypes.Count() == 0)
            {
                // Extension assemblies must contain a class that implements IExtension
                System.Console.WriteLine($"Failed to configure extension because assembly '{assembly.GetName()}' does not contain a type that implements IExtension.");
                continue;
            }

            if (extensionTypes.Count() > 1)
            {
                // Don't register the extension if it contains more than one IExtension class.
                // We can't tell which is the right one to load, so just log an error and skip to the next extension.
                System.Console.WriteLine($"Failed to configure extension because assembly '{assembly.GetName()}' contains multiple types that implement IExtension.");
                continue;
            }

            var extensionType = extensionTypes.First();
            try
            {
                // Create an instance of the class
                var instance = Activator.CreateInstance(extensionType) as IExtension;
                if (instance != null)
                {
                    instance.ConfigureServices(extensionServices);
                }
            }
            catch (Exception ex)
            {
                // Log the exception and continue
                System.Console.WriteLine($"Error initializing extension {extensionType.Name}: {ex.Message}");
            }
        }

        extensionServices.PopulateServices(services);
    }
}
