using Celbridge.Extensions;
using Celbridge.Workspace;

namespace Celbridge.Documents.Extensions;

/// <summary>
/// Factory for creating ExtensionDocumentView instances for custom (WebView2-based)
/// extension editors. One instance per discovered manifest of type "custom".
/// </summary>
public class CustomExtensionEditorFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ExtensionManifest _manifest;
    private readonly IWorkspaceFeatures? _workspaceFeatures;

    public override IReadOnlyList<string> SupportedExtensions =>
        _manifest.FileTypes.Select(ft => ft.Extension).ToList();

    public override int Priority => _manifest.Priority;

    public CustomExtensionEditorFactory(
        IServiceProvider serviceProvider,
        ExtensionManifest manifest,
        IWorkspaceFeatures? workspaceFeatures = null)
    {
        _serviceProvider = serviceProvider;
        _manifest = manifest;
        _workspaceFeatures = workspaceFeatures;
    }

    public override bool CanHandle(ResourceKey fileResource, string filePath)
    {
        if (!string.IsNullOrEmpty(_manifest.FeatureFlag) &&
            _workspaceFeatures is not null &&
            !_workspaceFeatures.IsEnabled(_manifest.FeatureFlag))
        {
            return false;
        }

        return base.CanHandle(fileResource, filePath);
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
#if WINDOWS
        var view = _serviceProvider.GetRequiredService<ExtensionDocumentView>();
        view.Manifest = _manifest;

        return Result<IDocumentView>.Ok(view);
#else
        return Result<IDocumentView>.Fail("Extension editors are only available on Windows");
#endif
    }
}
