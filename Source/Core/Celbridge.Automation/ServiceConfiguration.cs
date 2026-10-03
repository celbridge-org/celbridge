using Celbridge.Automation.Services;

namespace Celbridge.Automation;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddSingleton<IAutomationService, AutomationService>();
    }
}
