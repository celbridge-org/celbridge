using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;

namespace Celbridge.Resources;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddSingleton<IResourceOperationService, ResourceOperationService>();
        services.AddSingleton<IFileTemplateService, FileTemplateService>();

        services.AddTransient<IResourceService, ResourceService>();
        services.AddTransient<IResourceRegistry, ResourceRegistry>();
        services.AddTransient<IResourceTransferService, ResourceTransferService>();
        services.AddTransient<IResourceRegistryDumper, ResourceRegistryDumper>();
        services.AddTransient<IResourceNameValidator, ResourceNameValidator>();
        services.AddTransient<IResourceMonitor, ResourceMonitor>();
        services.AddTransient<AddResourceHelper>();

        //
        // Register commands
        //

        services.AddTransient<IUpdateResourcesCommand, UpdateResourcesCommand>();
        services.AddTransient<IAddResourceCommand, AddResourceCommand>();
        services.AddTransient<IDeleteResourceCommand, DeleteResourceCommand>();
        services.AddTransient<ICopyResourceCommand, CopyResourceCommand>();
        services.AddTransient<ITransferResourcesCommand, TransferResourcesCommand>();
    }
}
