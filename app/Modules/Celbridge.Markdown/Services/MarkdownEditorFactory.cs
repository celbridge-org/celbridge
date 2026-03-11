using Celbridge.Markdown.Views;

namespace Celbridge.Markdown.Services;

/// <summary>
/// Factory for creating Markdown document views using Monaco editor.
/// Handles .md and .markdown files with source-first editing.
/// </summary>
public class MarkdownEditorFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".md", ".markdown"];

    public MarkdownEditorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
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
