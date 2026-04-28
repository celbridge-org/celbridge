using Celbridge.Documents;
using Celbridge.WebView.Views;
using Microsoft.Extensions.Localization;

namespace Celbridge.WebView.Services;

/// <summary>
/// Factory for creating WebView document views.
/// Handles .webview files which embed external web URLs in the editor.
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
        return Result<IDocumentView>.Ok(view);
    }
}
