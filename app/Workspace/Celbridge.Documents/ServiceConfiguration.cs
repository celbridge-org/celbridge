using Celbridge.Documents.Commands;
using Celbridge.Documents.Services;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;

namespace Celbridge.Documents;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddTransient<IDocumentsService, DocumentsService>();
        services.AddTransient<FileTypeHelper>();

        //
        // Register views
        //

        services.AddTransient<IDocumentsPanel, DocumentsPanel>();
        services.AddTransient<TextBoxDocumentView>();
        services.AddTransient<WebAppDocumentView>();
        services.AddTransient<MonacoEditorView>();
        services.AddTransient<FileViewerDocumentView>();
        services.AddTransient<TextEditorDocumentView>();
        services.AddTransient<SpreadsheetDocumentView>();
        services.AddTransient<NoteDocumentView>();

        //
        // Register view models
        //

        services.AddTransient<DocumentsPanelViewModel>();
        services.AddTransient<DocumentTabViewModel>();
        services.AddTransient<DefaultDocumentViewModel>();
        services.AddTransient<WebAppDocumentViewModel>();
        services.AddTransient<MonacoEditorViewModel>();
        services.AddTransient<FileViewerDocumentViewModel>();
        services.AddTransient<TextEditorDocumentViewModel>();
        services.AddTransient<SpreadsheetDocumentViewModel>();
        services.AddTransient<NoteDocumentViewModel>();

        //
        // Register commands
        //

        services.AddTransient<IOpenDocumentCommand, OpenDocumentCommand>();
        services.AddTransient<ICloseDocumentCommand, CloseDocumentCommand>();
        services.AddTransient<ISelectDocumentCommand, SelectDocumentCommand>();
        services.AddTransient<IResetSectionsCommand, ResetSectionsCommand>();
    }
}
