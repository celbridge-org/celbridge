namespace Celbridge.Packages;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<IPackageLocalizationService, PackageLocalizationService>();
        services.AddTransient<IPackageService, PackageService>();
    }
}
