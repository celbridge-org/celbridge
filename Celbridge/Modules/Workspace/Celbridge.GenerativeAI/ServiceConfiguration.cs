using Celbridge.GenerativeAI.Commands;
using Celbridge.Modules;


namespace Celbridge.GenerativeAI;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IModuleServiceCollection config)
    {
        //
        // Register commands
        //

        config.AddTransient<IMakeTextCommand, MakeTextCommand>();

        //
        // Register services
        //

        config.AddTransient<IGenerativeAIService, GenerativeAIService>();
        config.AddTransient<IGenerativeAIProvider, OpenAIProvider>();
    }
}
