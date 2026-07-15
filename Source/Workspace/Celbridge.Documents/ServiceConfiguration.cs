using Celbridge.Documents.Commands;
using Celbridge.Documents.Services;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Workspace;

namespace Celbridge.Documents;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddTransient<IDocumentsService, DocumentsService>();
        services.AddTransient<IUtilityService, UtilityService>();

        //
        // Register views
        //

        services.AddTransient<IDocumentsPanel, DocumentsPanel>();
        services.AddTransient<TextBoxDocumentView>();
        services.AddTransient<CustomDocumentView>();
        services.AddTransient<CustomUtilityView>();

        //
        // Register view models
        //

        services.AddTransient<DocumentsPanelViewModel>();
        services.AddTransient<DocumentTabViewModel>();
        services.AddTransient<DefaultDocumentViewModel>();
        services.AddTransient<CustomDocumentViewModel>();

        //
        // Register commands
        //

        services.AddTransient<IOpenDocumentCommand, OpenDocumentCommand>();
        services.AddTransient<IShowUtilityCommand, ShowUtilityCommand>();
        services.AddTransient<IDockUtilityCommand, DockUtilityCommand>();
        services.AddTransient<ICloseDocumentCommand, CloseDocumentCommand>();
        services.AddTransient<IActivateDocumentCommand, ActivateDocumentCommand>();
        services.AddTransient<IResetSectionsCommand, ResetSectionsCommand>();
        services.AddTransient<IGetDocumentStateCommand, GetDocumentStateCommand>();
        services.AddTransient<IGetUtilitiesStateCommand, GetUtilitiesStateCommand>();
    }
}
