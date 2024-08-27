using Celbridge.Extensions;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Documents.Services;

namespace Celbridge.Documents;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        config.AddTransient<DocumentsPanel>();
        config.AddTransient<DocumentsPanelViewModel>();
        config.AddTransient<DocumentTabViewModel>();
        config.AddTransient<WebDocumentViewModel>();
        config.AddTransient<TextDocumentViewModel>();

        config.AddTransient<IDocumentsService, DocumentsService>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }
}
