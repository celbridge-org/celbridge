using Celbridge.Documents;
using Celbridge.Modules;
using Celbridge.WebView.Services;
using Celbridge.WebView.ViewModels;
using Celbridge.WebView.Views;
using Microsoft.Extensions.Localization;

namespace Celbridge.WebView;

public class Module : IModule
{
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
        services.AddTransient<WebViewDocumentSettingsViewModel>();
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
        ];
    }
}
