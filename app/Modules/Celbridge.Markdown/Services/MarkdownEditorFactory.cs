using Celbridge.Code.Views;
using Celbridge.Markdown.Services;
using Celbridge.Markdown.Views;
using Microsoft.Extensions.Localization;

namespace Celbridge.Markdown.Services;

/// <summary>
/// Factory for creating Markdown document views using the unified CodeEditorDocumentView.
/// Configures preview rendering and snippet toolbar for Markdown editing.
/// </summary>
public class MarkdownEditorFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IStringLocalizer _stringLocalizer;

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".md", ".markdown"];

    public MarkdownEditorFactory(IServiceProvider serviceProvider, IStringLocalizer stringLocalizer)
    {
        _serviceProvider = serviceProvider;
        _stringLocalizer = stringLocalizer;
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
#if WINDOWS
        var view = _serviceProvider.GetRequiredService<CodeEditorDocumentView>();

        // Configure the preview renderer
        var previewRenderer = new MarkdownPreviewRenderer();
        view.ConfigurePreview(previewRenderer);

        // Default to Preview mode for Markdown files
        view.InitialViewMode = SplitEditorViewMode.Preview;

        // Set up localized view mode tooltips
        view.PreviewModeTooltip = _stringLocalizer.GetString("Markdown_ViewMode_Preview");
        view.SplitModeTooltip = _stringLocalizer.GetString("Markdown_ViewMode_Split");
        view.SourceModeTooltip = _stringLocalizer.GetString("Markdown_ViewMode_Source");

        // Add the Markdown snippet toolbar
        var snippetToolbar = MarkdownSnippetToolbar.Create(view, _stringLocalizer);
        view.CustomToolbar = snippetToolbar;

        return Result<IDocumentView>.Ok(view);
#else
        // On non-Windows platforms, Markdown editor is not available
        return Result<IDocumentView>.Fail("Markdown editor is not available on this platform");
#endif
    }
}
