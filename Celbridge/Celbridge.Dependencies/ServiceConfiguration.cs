using Celbridge.BaseLibrary.Extensions;
using Celbridge.Dependencies.Extensions;

namespace Celbridge.Dependencies;

/// <summary>
/// Configures the dependency injection framework to support all required services.
/// </summary>
public class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services, List<IExtension> extensions)
    {
        CommonServices.ServiceConfiguration.ConfigureServices(services);
        CommonUI.ServiceConfiguration.ConfigureServices(services);
        CommonViewModels.ServiceConfiguration.ConfigureServices(services);

        // Register the services provided by each extension with the dependency injection framework.
        var extensionServices = new ExtensionServiceCollection();
        foreach (var extension in extensions)
        {
            extension.ConfigureServices(extensionServices);
        }
        extensionServices.PopulateServices(services);
    }
}