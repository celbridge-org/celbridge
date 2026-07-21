using Celbridge.Utilities.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Utilities;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ITextBinarySniffer, TextBinarySniffer>();
        services.AddSingleton<IPlatformInfo, PlatformInfo>();
        services.AddSingleton<IAppEnvironment, AppEnvironment>();
    }
}
