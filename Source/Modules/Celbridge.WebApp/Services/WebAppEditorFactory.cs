using Celbridge.Documents;
using Celbridge.WebApp.Views;
using Microsoft.Extensions.Localization;

namespace Celbridge.WebApp.Services;

/// <summary>
/// Factory for creating WebApp document views.
/// Handles .webapp files which are web applications embedded in the editor.
/// </summary>
public class WebAppEditorFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IStringLocalizer _stringLocalizer;

    public override DocumentEditorId EditorId { get; } = new("celbridge.webapp-editor");

    public override string DisplayName => _stringLocalizer.GetString("DocumentEditor_WebAppEditor");

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".webapp"];

    public WebAppEditorFactory(IServiceProvider serviceProvider, IStringLocalizer stringLocalizer)
    {
        _serviceProvider = serviceProvider;
        _stringLocalizer = stringLocalizer;
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        var view = _serviceProvider.GetRequiredService<WebAppDocumentView>();
        return Result<IDocumentView>.Ok(view);
    }
}
