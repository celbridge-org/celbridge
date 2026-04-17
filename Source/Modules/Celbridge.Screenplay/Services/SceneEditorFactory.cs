using Celbridge.Documents;
using Celbridge.Screenplay.Views;
using Microsoft.Extensions.Localization;

namespace Celbridge.Screenplay.Services;

/// <summary>
/// Factory for creating Scene document views.
/// Handles .scene files which display a formatted HTML preview of screenplay scenes.
/// </summary>
public class SceneEditorFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IStringLocalizer _stringLocalizer;

    public override DocumentEditorId EditorId { get; } = new("celbridge.scene-editor");

    public override string DisplayName => _stringLocalizer.GetString("DocumentEditor_SceneEditor");

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".scene"];

    public override EditorPriority Priority => EditorPriority.Specialized;

    public SceneEditorFactory(IServiceProvider serviceProvider, IStringLocalizer stringLocalizer)
    {
        _serviceProvider = serviceProvider;
        _stringLocalizer = stringLocalizer;
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        var view = _serviceProvider.GetRequiredService<SceneDocumentView>();
        return Result<IDocumentView>.Ok(view);
    }
}
