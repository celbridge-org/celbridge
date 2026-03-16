namespace Celbridge.Extensions;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register extension infrastructure
        //

        services.AddTransient<ExtensionLocalizationService>();
        services.AddTransient<IExtensionService, ExtensionService>();
    }
}
