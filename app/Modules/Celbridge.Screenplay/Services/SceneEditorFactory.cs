using Celbridge.Documents;
using Celbridge.Screenplay.Views;

namespace Celbridge.Screenplay.Services;

/// <summary>
/// Factory for creating Scene document views.
/// Handles .scene files which display a formatted HTML preview of screenplay scenes.
/// </summary>
public class SceneEditorFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".scene"];

    /// <summary>
    /// Higher priority than the default CodeEditorFactory to ensure .scene files
    /// use SceneDocumentView instead of the text editor.
    /// </summary>
    public override int Priority => 10;

    public SceneEditorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        var view = _serviceProvider.GetRequiredService<SceneDocumentView>();
        return Result<IDocumentView>.Ok(view);
    }
}
