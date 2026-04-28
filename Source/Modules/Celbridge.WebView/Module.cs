using Celbridge.Activities;
using Celbridge.Documents;
using Celbridge.Modules;
using Celbridge.Packages;
using Celbridge.WebView.Services;
using Celbridge.WebView.ViewModels;
using Celbridge.WebView.Views;
using Microsoft.Extensions.Localization;

namespace Celbridge.WebView;

public class Module : IModule
{
    public IReadOnlyList<string> SupportedActivities { get; } = new List<string>();

    public void ConfigureServices(IModuleServiceCollection services)
    {
        //
        // Register views
        //

        services.AddTransient<WebViewDocumentView>();

        //
        // Register view models
        //

        services.AddTransient<WebViewDocumentViewModel>();

        //
        // Register navigation policy helper
        //

        services.AddTransient<IWebViewNavigationPolicy, WebViewNavigationPolicy>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }

    public IReadOnlyList<IDocumentEditorFactory> CreateDocumentEditorFactories(IServiceProvider serviceProvider)
    {
        var stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();
        return
        [
            new WebViewEditorFactory(serviceProvider, stringLocalizer),
            new HtmlViewerEditorFactory(serviceProvider, stringLocalizer),
        ];
    }

    public Result<IActivity> CreateActivity(string activityName)
    {
        return Result<IActivity>.Fail();
    }

    public IReadOnlyList<BundledPackageDescriptor> GetBundledPackages()
    {
        return Array.Empty<BundledPackageDescriptor>();
    }
}
