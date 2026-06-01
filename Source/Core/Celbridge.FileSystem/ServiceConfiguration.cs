using Celbridge.FileSystem.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.FileSystem;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ILocalFileSystem, LocalFileSystem>();
    }
}
