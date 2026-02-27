using Celbridge.Documents;
using Celbridge.Markdown.Views;

namespace Celbridge.Markdown.Services;

/// <summary>
/// Factory for creating Markdown document views.
/// </summary>
public class MarkdownEditorFactory : IDocumentEditorFactory
{
    private readonly IServiceProvider _serviceProvider;

    public IReadOnlyList<string> SupportedExtensions { get; } = new List<string> { ".md" };

    public int Priority => 0;

    public MarkdownEditorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public bool CanHandle(ResourceKey fileResource, string filePath)
    {
        return true;
    }

    public Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
#if WINDOWS
        var view = _serviceProvider.GetRequiredService<MarkdownDocumentView>();
        return Result<IDocumentView>.Ok(view);
#else
        // On non-Windows platforms, Markdown editor is not available
        return Result<IDocumentView>.Fail("Markdown editor is not available on this platform");
#endif
    }
}
