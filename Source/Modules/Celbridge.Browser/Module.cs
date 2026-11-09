using Celbridge.Documents;
using Celbridge.Modules;
using Celbridge.Browser.Services;
using Celbridge.Browser.ViewModels;
using Celbridge.Browser.Views;
using Microsoft.Extensions.Localization;

namespace Celbridge.Browser;

public class Module : IModule
{
    public void ConfigureServices(IModuleServiceCollection services)
    {
        //
        // Register views
        //

        services.AddTransient<BrowserDocumentView>();

        //
        // Register view models
        //

        services.AddTransient<BrowserDocumentViewModel>();
        services.AddTransient<BrowserDocumentSettingsViewModel>();
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
            new BrowserEditorFactory(serviceProvider, stringLocalizer),
        ];
    }
}
