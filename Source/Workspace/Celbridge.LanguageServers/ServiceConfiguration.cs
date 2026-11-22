using Celbridge.Dependencies;
using Celbridge.LanguageServers.Services;
using Celbridge.WebHost;

namespace Celbridge.LanguageServers;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddTransient<ILanguageServerService, LanguageServerService>();
        services.AddSingleton<ILanguageServerLauncher, LanguageServerLauncher>();
        services.AddSingleton<ICustomEditorChannelProvider, LanguageChannelProvider>();
        services.AddSingleton<ILanguageServerWorkspace, LanguageServerWorkspace>();

        //
        // Register the TypeScript language server
        //

        services.AddSingleton<TypeScriptDependency>();
        services.AddSingleton<IFeatureDependencies, TypeScriptFeatureDependencies>();
        services.AddSingleton<ILanguageServerDefinition, TypeScriptServerDefinition>();

        //
        // Register the Python language server. The Python module installs the server.
        //

        services.AddSingleton<ILanguageServerDefinition, PythonServerDefinition>();
    }
}
