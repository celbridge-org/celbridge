using Celbridge.Documents.Views;

namespace Celbridge.Documents.Services;

/// <summary>
/// Factory for creating WebApp document views.
/// Handles .webapp files which are web applications embedded in the editor.
/// </summary>
public class WebAppEditorFactory : IDocumentEditorFactory
{
    private readonly IServiceProvider _serviceProvider;

    public IReadOnlyList<string> SupportedExtensions { get; } = new List<string> { ".webapp" };

    public int Priority => 0;

    public WebAppEditorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public bool CanHandle(ResourceKey fileResource, string filePath)
    {
        return true;
    }

    public Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        var view = _serviceProvider.GetRequiredService<WebAppDocumentView>();
        return Result<IDocumentView>.Ok(view);
    }
}
