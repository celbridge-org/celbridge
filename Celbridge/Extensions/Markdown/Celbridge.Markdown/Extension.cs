using Celbridge.Core;
using Celbridge.Modules;
using Celbridge.Messaging;
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

