using Celbridge.GenerativeAI.Commands;
using Microsoft.Extensions.DependencyInjection;


namespace Celbridge.GenerativeAI;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register commands
        //

        services.AddTransient<IMakeTextCommand, MakeTextCommand>();

        //
        // Register services
        //

        services.AddTransient<IGenerativeAIService, GenerativeAIService>();
        services.AddTransient<IGenerativeAIProvider, OpenAIProvider>();
    }
}
