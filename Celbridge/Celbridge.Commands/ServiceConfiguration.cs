using Celbridge.BaseLibrary.Commands.Project;
using Celbridge.Commands.Project;
using Celbridge.Services.Commands;

namespace Celbridge.Commands;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ICommandService, CommandService>();
        services.AddTransient<ILoadProjectCommand, LoadProjectCommand>();
        services.AddTransient<IUnloadProjectCommand, UnloadProjectCommand>();
    }

    public static void Initialize()
    {}
}
