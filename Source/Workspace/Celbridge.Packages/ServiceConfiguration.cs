namespace Celbridge.Packages;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<IPackageLocalizationService, PackageLocalizationService>();
        services.AddTransient<PackageRegistry>();
        services.AddTransient<IPackageService, PackageService>();
        services.AddSingleton<IPackageApiClient, PackageApiClient>();
        services.AddSingleton<IPageApiClient, PageApiClient>();
    }
}
