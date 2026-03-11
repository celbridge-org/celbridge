using Celbridge.Notes.Views;
using Celbridge.Workspace;

namespace Celbridge.Notes.Services;

/// <summary>
/// Factory for creating Note document views.
/// </summary>
public class NoteEditorFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceFeatures _workspaceFeatures;

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".note"];

    public NoteEditorFactory(
        IServiceProvider serviceProvider,
        IWorkspaceFeatures workspaceFeatures)
    {
        _serviceProvider = serviceProvider;
        _workspaceFeatures = workspaceFeatures;
    }

    public override bool CanHandle(ResourceKey fileResource, string filePath)
    {
        if (!_workspaceFeatures.IsEnabled(FeatureFlags.NoteEditor))
        {
            return false;
        }

        return base.CanHandle(fileResource, filePath);
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
#if WINDOWS
        var view = _serviceProvider.GetRequiredService<NoteDocumentView>();
        return view;
#else
        // On non-Windows platforms, Note editor is not available
        return Result<IDocumentView>.Fail("Note editor is not available on this platform");
#endif
    }
}
