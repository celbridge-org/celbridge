using Celbridge.Documents;
using Celbridge.WebView.Views;
using Microsoft.Extensions.Localization;

namespace Celbridge.WebView.Services;

/// <summary>
/// Factory for the .webview editor. Produces a WebViewDocumentView configured for
/// the external-URL role; the URL is read from the .webview document's JSON body.
/// </summary>
public class WebViewEditorFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IStringLocalizer _stringLocalizer;

    public override DocumentEditorId EditorId { get; } = new("celbridge.webview-editor");

    public override string DisplayName => _stringLocalizer.GetString("DocumentEditor_WebViewEditor");

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".webview"];

    public WebViewEditorFactory(IServiceProvider serviceProvider, IStringLocalizer stringLocalizer)
    {
        _serviceProvider = serviceProvider;
        _stringLocalizer = stringLocalizer;
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        var view = _serviceProvider.GetRequiredService<WebViewDocumentView>();
        view.Options = new WebViewDocumentOptions(
            WebViewDocumentRole.ExternalUrl,
            InterceptTopFrameNavigation: false);

        return Result<IDocumentView>.Ok(view);
    }
}
