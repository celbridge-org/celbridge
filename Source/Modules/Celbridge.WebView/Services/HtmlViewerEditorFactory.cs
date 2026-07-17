using Celbridge.Documents;
using Celbridge.WebView.Views;
using Microsoft.Extensions.Localization;

namespace Celbridge.WebView.Services;

/// <summary>
/// Factory for the built-in HTML viewer. Claims .html and .htm ahead of the code editor in the
/// built-in host order, making the viewer the default editor for those extensions.
/// </summary>
public class HtmlViewerEditorFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IStringLocalizer _stringLocalizer;

    public override EditorInstanceId EditorId { get; } = new("celbridge.html-viewer");

    public override string DisplayName => _stringLocalizer.GetString("DocumentEditor_HtmlViewer");

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".html", ".htm"];

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
        view.EditorId = EditorId;

        return Result<IDocumentView>.Ok(view);
    }
}
