using Celbridge.Extensions;
using Celbridge.Services;

namespace Celbridge;

/// <summary>
/// Configures the dependency injection framework to support all required services.
/// </summary>
public class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services, List<IExtension> extensions)
    {
        CoreServices.ServiceConfiguration.ConfigureServices(services);

        // Register the services provided by each extension with the dependency injection framework.
        var extensionServices = new ExtensionServiceCollection();
        foreach (var extension in extensions)
        {
            extension.ConfigureServices(extensionServices);
        }
        extensionServices.PopulateServices(services);
    }
}
