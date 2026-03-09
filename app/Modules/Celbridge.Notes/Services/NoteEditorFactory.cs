using Celbridge.Notes.Views;

namespace Celbridge.Notes.Services;

/// <summary>
/// Factory for creating Note document views.
/// </summary>
public class NoteEditorFactory : IDocumentEditorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceFeatures _workspaceFeatures;

    public IReadOnlyList<string> SupportedExtensions { get; } = [".note"];

    public int Priority => 0;

    public NoteEditorFactory(
        IServiceProvider serviceProvider,
        IWorkspaceFeatures workspaceFeatures)
    {
        _serviceProvider = serviceProvider;
        _workspaceFeatures = workspaceFeatures;
    }

    public bool CanHandle(ResourceKey fileResource, string filePath)
    {
        if (!_workspaceFeatures.IsEnabled(FeatureFlags.NoteEditor))
        {
            return false;
        }

        var extension = Path.GetExtension(fileResource.ToString()).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    public Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
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
