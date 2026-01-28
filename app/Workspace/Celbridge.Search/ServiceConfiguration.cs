using Celbridge.Search.Services;
using Celbridge.Search.ViewModels;
using Celbridge.Search.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Search;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddTransient<ISearchService, SearchService>();

        //
        // Register views
        //

        services.AddTransient<ISearchPanel, SearchPanel>();

        //
        // Register view models
        //

        services.AddTransient<SearchPanelViewModel>();
    }
}
