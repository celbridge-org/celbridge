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

        //
        // Register views
        //

        services.AddTransient<IDocumentsPanel, DocumentsPanel>();
        services.AddTransient<TextBoxDocumentView>();
        services.AddTransient<ContributionDocumentView>();

        //
        // Register view models
        //

        services.AddTransient<DocumentsPanelViewModel>();
        services.AddTransient<DocumentTabViewModel>();
        services.AddTransient<DefaultDocumentViewModel>();
        services.AddTransient<ContributionDocumentViewModel>();

        //
        // Register commands
        //

        services.AddTransient<IOpenDocumentCommand, OpenDocumentCommand>();
        services.AddTransient<ICloseDocumentCommand, CloseDocumentCommand>();
        services.AddTransient<IActivateDocumentCommand, ActivateDocumentCommand>();
        services.AddTransient<IResetSectionsCommand, ResetSectionsCommand>();
        services.AddTransient<IGetDocumentStateCommand, GetDocumentStateCommand>();
        services.AddTransient<ISetEditorPreferenceCommand, SetEditorPreferenceCommand>();
    }
}
