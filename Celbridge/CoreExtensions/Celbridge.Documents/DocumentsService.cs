using Celbridge.BaseLibrary.Documents;
using Celbridge.Documents.Views;

namespace Celbridge.Documents;

public class DocumentsService : IDocumentsService
{
    private readonly IServiceProvider _serviceProvider;

    public DocumentsService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object CreateDocumentsPanel()
    {
        return _serviceProvider.GetRequiredService<DocumentsPanel>();
    }
}
