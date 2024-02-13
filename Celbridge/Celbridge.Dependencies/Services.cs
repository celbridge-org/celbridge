using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Settings;
using Celbridge.CommonServices.LiteDB;
using Celbridge.CommonServices.Logging;
using Celbridge.CommonServices.Messaging;
using Celbridge.CommonServices.Settings;
using System.Reflection;

namespace Celbridge.Dependencies;

public class Services
{
    public static void Configure(IServiceCollection services, List<Assembly> extensionAssemblies)
    {
        ConfigureCommonServices(services);
        ConfigureExtensionServices(services, extensionAssemblies);

        // Internal services
        services.AddSingleton<LiteDBService>();
        services.AddTransient<LiteDBInstance>();
    }

    private static void ConfigureCommonServices(IServiceCollection services)
    {
        services.AddTransient<ISettingsContainer, SettingsContainer>();
        services.AddSingleton<IEditorSettings, EditorSettings>();
        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<ILoggingService, LoggingService>();
    }

    private static void ConfigureExtensionServices(IServiceCollection services, List<Assembly> extensionAssemblies)
    {
        var configuration = new ServiceConfiguration();

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
                    instance.ConfigureServices(configuration);
                }
            }
            catch (Exception ex)
            {
                // Log the exception and continue
                System.Console.WriteLine($"Error initializing extension {extensionType.Name}: {ex.Message}");
            }
        }

        configuration.ConfigureServices(services);
    }
}
