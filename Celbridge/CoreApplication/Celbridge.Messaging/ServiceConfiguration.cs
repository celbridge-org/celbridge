using Celbridge.BaseLibrary.Messaging;
using Celbridge.Messaging.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Messaging;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //
        services.AddSingleton<IMessengerService, MessengerService>();
    }
}