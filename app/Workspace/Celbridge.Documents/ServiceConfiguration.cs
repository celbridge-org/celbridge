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

        // FileTypeHelper must be singleton because it's initialized by DocumentsService
        // and shared across all document editor factories
        services.AddSingleton<FileTypeHelper>();

        //
        // Register views
        //

        services.AddTransient<IDocumentsPanel, DocumentsPanel>();
        services.AddTransient<TextBoxDocumentView>();

        //
        // Register view models
        //

        services.AddTransient<DocumentsPanelViewModel>();
        services.AddTransient<DocumentTabViewModel>();
        services.AddTransient<DefaultDocumentViewModel>();

        //
        // Register commands
        //

        services.AddTransient<IOpenDocumentCommand, OpenDocumentCommand>();
        services.AddTransient<ICloseDocumentCommand, CloseDocumentCommand>();
        services.AddTransient<ISelectDocumentCommand, SelectDocumentCommand>();
        services.AddTransient<IResetSectionsCommand, ResetSectionsCommand>();
    }
}
