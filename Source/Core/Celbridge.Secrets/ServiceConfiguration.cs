using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Secrets;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISecretRegistry, SecretRegistry>();
    }
}
