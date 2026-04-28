using Celbridge.Documents;
using Celbridge.WebView.Views;
using Microsoft.Extensions.Localization;

namespace Celbridge.WebView.Services;

/// <summary>
/// Factory for the built-in HTML viewer. Claims .html and .htm at Specialized priority
/// so the viewer is the default editor for those extensions; the code editor's General
/// priority claim remains in the registry as a multi-claimant alternate.
/// </summary>
public class HtmlViewerEditorFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IStringLocalizer _stringLocalizer;

    public override DocumentEditorId EditorId { get; } = new("celbridge.html-viewer");

    public override string DisplayName => _stringLocalizer.GetString("DocumentEditor_HtmlViewer");

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".html", ".htm"];

    public override EditorPriority Priority => EditorPriority.Specialized;

    public HtmlViewerEditorFactory(IServiceProvider serviceProvider, IStringLocalizer stringLocalizer)
    {
        _serviceProvider = serviceProvider;
        _stringLocalizer = stringLocalizer;
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        var view = _serviceProvider.GetRequiredService<WebViewDocumentView>();
        view.Options = new WebViewDocumentOptions(
            WebViewDocumentRole.HtmlViewer,
            InterceptTopFrameNavigation: true);

        return Result<IDocumentView>.Ok(view);
    }
}
