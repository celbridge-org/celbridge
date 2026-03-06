using Celbridge.Notes.Views;

namespace Celbridge.Notes.Services;

/// <summary>
/// Factory for creating Note document views.
/// Only handles .note files when the EnableNotesEditor feature flag is enabled.
/// </summary>
public class NoteEditorFactory : IDocumentEditorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IFeatureFlagService _featureFlagService;

    public IReadOnlyList<string> SupportedExtensions { get; } = [".note"];

    public int Priority => 0;

    public NoteEditorFactory(
        IServiceProvider serviceProvider,
        IFeatureFlagService featureFlagService)
    {
        _serviceProvider = serviceProvider;
        _featureFlagService = featureFlagService;
    }

    public bool CanHandle(ResourceKey fileResource, string filePath)
    {
        if (!_featureFlagService.IsEnabled("EnableNotesEditor"))
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
        return Result<IDocumentView>.Ok(view);
#else
        // On non-Windows platforms, Note editor is not available
        return Result<IDocumentView>.Fail("Note editor is not available on this platform");
#endif
    }
}
