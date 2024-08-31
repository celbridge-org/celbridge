using Celbridge.Documents.Commands;
using Celbridge.Documents.Services;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Extensions;

namespace Celbridge.Documents;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        //
        // Register services
        //

        config.AddTransient<IDocumentsService, DocumentsService>();

        //
        // Register UI elements
        //

        config.AddTransient<DocumentsPanel>();

        //
        // Register ViewModels
        //

        config.AddTransient<DocumentsPanelViewModel>();
        config.AddTransient<DocumentTabViewModel>();
        config.AddTransient<WebDocumentViewModel>();
        config.AddTransient<TextDocumentViewModel>();

        //
        // Register commands
        //

        config.AddTransient<IOpenDocumentCommand, OpenDocumentCommand>();
        config.AddTransient<ICloseDocumentCommand, CloseDocumentCommand>();
        config.AddTransient<ISelectDocumentCommand, SelectDocumentCommand>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }
}
