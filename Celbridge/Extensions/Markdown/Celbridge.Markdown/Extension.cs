using Celbridge.Core;
using Celbridge.Foundation;
using Celbridge.Messaging;
using Celbridge.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Markdown;

public class Extension : IModule
{
    public void ConfigureServices(IModuleServiceCollection config)
    {
        //
        // Register services
        //
    }

    public Result Initialize()
    {
        var messengerService = ServiceLocator.ServiceProvider.GetRequiredService<IMessengerService>();

        return Result.Ok();
    }
}

