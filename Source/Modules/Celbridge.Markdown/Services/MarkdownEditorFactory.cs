using Celbridge.Code.Views;
using Celbridge.Markdown.Views;
using Microsoft.Extensions.Localization;

namespace Celbridge.Markdown.Services;

/// <summary>
/// Factory for creating Markdown document views using the unified CodeEditorDocumentView.
/// Enables the in-editor markdown preview and attaches the snippet toolbar.
/// </summary>
public class MarkdownEditorFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IStringLocalizer _stringLocalizer;

    public override DocumentEditorId EditorId { get; } = new("celbridge.markdown-editor");

    public override string DisplayName => _stringLocalizer.GetString("DocumentEditor_MarkdownEditor");

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".md", ".markdown"];

    public MarkdownEditorFactory(IServiceProvider serviceProvider, IStringLocalizer stringLocalizer)
    {
        _serviceProvider = serviceProvider;
        _stringLocalizer = stringLocalizer;
    }

    // URL of the markdown preview ES module served by Monaco's virtual host.
    // The split-editor preview contract is generic: point this at any module that exports
    // the initialize/render/setBasePath/setScrollPercentage/getScrollPercentage functions.
    // Must use the same scheme as Monaco's host page so the iframe is same-origin and the
    // parent can manipulate its contentDocument directly.
    private const string MarkdownPreviewRendererUrl =
        "http://monaco.celbridge/markdown-preview/preview-module.js";

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
#if WINDOWS
        var view = _serviceProvider.GetRequiredService<CodeEditorDocumentView>();

        // Attach the markdown preview renderer
        view.SetPreviewRenderer(MarkdownPreviewRendererUrl);

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
