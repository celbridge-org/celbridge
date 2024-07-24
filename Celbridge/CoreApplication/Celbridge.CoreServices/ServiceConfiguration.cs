namespace Celbridge.CoreServices;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        Commands.ServiceConfiguration.ConfigureServices(services);
        Logging.ServiceConfiguration.ConfigureServices(services);
        Messaging.ServiceConfiguration.ConfigureServices(services);
        Settings.ServiceConfiguration.ConfigureServices(services);
        UserInterface.ServiceConfiguration.ConfigureServices(services);
        Utilities.ServiceConfiguration.ConfigureServices(services);
    }
}