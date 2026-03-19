using Celbridge.Documents;
using Celbridge.WebApp.Views;

namespace Celbridge.WebApp.Services;

/// <summary>
/// Factory for creating WebApp document views.
/// Handles .webapp files which are web applications embedded in the editor.
/// </summary>
public class WebAppEditorFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".webapp"];

    public WebAppEditorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        var view = _serviceProvider.GetRequiredService<WebAppDocumentView>();
        return Result<IDocumentView>.Ok(view);
    }
}
