using Celbridge.Documents.Commands;
using Celbridge.Documents.Services;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Modules;

namespace Celbridge.Documents;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IModuleServiceCollection config)
    {
        //
        // Register services
        //

        config.AddTransient<IDocumentsService, DocumentsService>();
        config.AddTransient<FileTypeHelper>();

        //
        // Register views
        //

        config.AddTransient<IDocumentsPanel, DocumentsPanel>();
        config.AddTransient<TextBoxDocumentView>();
        config.AddTransient<WebPageDocumentView>();
        config.AddTransient<MonacoEditorView>();
        config.AddTransient<FileViewerDocumentView>();
        config.AddTransient<EditorPreviewView>();
        config.AddTransient<TextEditorDocumentView>();

        //
        // Register view models
        //

        config.AddTransient<DocumentsPanelViewModel>();
        config.AddTransient<DocumentTabViewModel>();
        config.AddTransient<DefaultDocumentViewModel>();
        config.AddTransient<WebPageDocumentViewModel>();
        config.AddTransient<MonacoEditorViewModel>();
        config.AddTransient<FileViewerDocumentViewModel>();
        config.AddTransient<EditorPreviewViewModel>();
        config.AddTransient<TextEditorDocumentViewModel>();

        //
        // Register commands
        //

        config.AddTransient<IOpenDocumentCommand, OpenDocumentCommand>();
        config.AddTransient<ICloseDocumentCommand, CloseDocumentCommand>();
        config.AddTransient<ISelectDocumentCommand, SelectDocumentCommand>();
    }
}
