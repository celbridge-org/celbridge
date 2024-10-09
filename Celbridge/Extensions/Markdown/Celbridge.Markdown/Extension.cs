using Celbridge.Core;
using Celbridge.Extensions;
using Celbridge.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Markdown;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
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

