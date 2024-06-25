using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Documents;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.BaseLibrary.Workspace;
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
