using Celbridge.Documents;
using Celbridge.Browser.Views;
using Microsoft.Extensions.Localization;

namespace Celbridge.Browser.Services;

/// <summary>
/// Factory for the .browser editor. Produces a BrowserDocumentView, which reads the bookmarks from the .browser
/// document's TOML body.
/// </summary>
public class BrowserEditorFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IStringLocalizer _stringLocalizer;

    public override EditorId EditorId { get; } = new("celbridge.browser-editor");

    public override string DisplayName => _stringLocalizer.GetString("DocumentEditor_BrowserEditor");

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".browser"];

    public BrowserEditorFactory(IServiceProvider serviceProvider, IStringLocalizer stringLocalizer)
    {
        _serviceProvider = serviceProvider;
        _stringLocalizer = stringLocalizer;
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        var view = _serviceProvider.GetRequiredService<BrowserDocumentView>();
        view.EditorId = EditorId;

        return Result<IDocumentView>.Ok(view);
    }
}
