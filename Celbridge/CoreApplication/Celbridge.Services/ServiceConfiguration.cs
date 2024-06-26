namespace Celbridge.Services;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        Celbridge.Settings.ServiceConfiguration.ConfigureServices(services);
        Celbridge.Messaging.ServiceConfiguration.ConfigureServices(services);
        Logging.ServiceConfiguration.ConfigureServices(services);
        UserInterface.ServiceConfiguration.ConfigureServices(services);
        Celbridge.Commands.ServiceConfiguration.ConfigureServices(services);
    }
}