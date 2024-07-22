using Celbridge.Extensions;
using Celbridge.Documents;
using Celbridge.UserInterface;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Workspace;
using Celbridge.Documents.Services;

namespace Celbridge.Documents;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        config.AddTransient<DocumentsPanel>();
        config.AddTransient<DocumentsPanelViewModel>();
        config.AddTransient<IDocumentsService, DocumentsService>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }
}
