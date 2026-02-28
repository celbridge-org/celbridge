using Celbridge.Documents;
using Celbridge.Screenplay.Views;

namespace Celbridge.Screenplay.Services;

/// <summary>
/// Factory for creating Scene document views.
/// Handles .scene files which display a formatted HTML preview of screenplay scenes.
/// </summary>
public class SceneEditorFactory : IDocumentEditorFactory
{
    private readonly IServiceProvider _serviceProvider;

    public IReadOnlyList<string> SupportedExtensions { get; } = new List<string> { ".scene" };

    /// <summary>
    /// Higher priority than the default CodeEditorFactory to ensure .scene files
    /// use SceneDocumentView instead of the text editor.
    /// </summary>
    public int Priority => 10;

    public SceneEditorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public bool CanHandle(ResourceKey fileResource, string filePath)
    {
        return true;
    }

    public Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        var view = _serviceProvider.GetRequiredService<SceneDocumentView>();
        return Result<IDocumentView>.Ok(view);
    }
}
