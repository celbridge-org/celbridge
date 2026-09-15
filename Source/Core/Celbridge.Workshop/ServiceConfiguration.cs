namespace Celbridge.Workshop;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddSingleton<IPackageApiClient, PackageApiClient>();
        services.AddSingleton<IPageApiClient, PageApiClient>();
    }
}
