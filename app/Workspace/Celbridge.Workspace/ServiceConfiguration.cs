namespace Celbridge.Workspace;

/// <summary>
/// Aggregator that configures all workspace sub-projects.
/// Implementation code lives in Celbridge.WorkspaceUI.
/// </summary>
public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Configure workspace sub-projects
        //

        Activities.ServiceConfiguration.ConfigureServices(services);
        Console.ServiceConfiguration.ConfigureServices(services);
        Documents.ServiceConfiguration.ConfigureServices(services);
        Entities.ServiceConfiguration.ConfigureServices(services);
        Explorer.ServiceConfiguration.ConfigureServices(services);
        GenerativeAI.ServiceConfiguration.ConfigureServices(services);
        Inspector.ServiceConfiguration.ConfigureServices(services);
        Python.ServiceConfiguration.ConfigureServices(services);
        Resources.ServiceConfiguration.ConfigureServices(services);
        Search.ServiceConfiguration.ConfigureServices(services);
        WorkspaceUI.ServiceConfiguration.ConfigureServices(services);
    }

    public static void Initialize()
    {
        WorkspaceUI.ServiceConfiguration.Initialize();
    }
}
