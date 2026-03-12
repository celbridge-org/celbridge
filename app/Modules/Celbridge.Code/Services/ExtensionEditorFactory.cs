using Celbridge.Code.Views;
using Celbridge.Documents.Extensions;
using Celbridge.Documents.Views;

namespace Celbridge.Code.Services;

/// <summary>
/// Factory for creating document views from extension manifests.
/// Routes by manifest type: custom -> ExtensionDocumentView, code -> CodeEditorDocumentView.
/// One instance per discovered manifest.
/// </summary>
public class ExtensionEditorFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ExtensionManifest _manifest;

    public override IReadOnlyList<string> SupportedExtensions => _manifest.Extensions;

    public override int Priority => _manifest.Priority;

    public ExtensionEditorFactory(IServiceProvider serviceProvider, ExtensionManifest manifest)
    {
        _serviceProvider = serviceProvider;
        _manifest = manifest;
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
#if WINDOWS
        return _manifest.Type switch
        {
            ExtensionEditorType.Custom => CreateCustomView(),
            ExtensionEditorType.Code => CreateCodeView(),
            _ => Result<IDocumentView>.Fail($"Unknown extension type: {_manifest.Type}")
        };
#else
        return Result<IDocumentView>.Fail("Extension editors are only available on Windows");
#endif
    }

    private Result<IDocumentView> CreateCustomView()
    {
        var view = _serviceProvider.GetRequiredService<ExtensionDocumentView>();
        view.Manifest = _manifest;

        return Result<IDocumentView>.Ok(view);
    }

    private Result<IDocumentView> CreateCodeView()
    {
        var view = _serviceProvider.GetRequiredService<CodeEditorDocumentView>();

        // Configure preview if the manifest declares it
        if (_manifest.Preview is not null)
        {
            var previewRenderer = new ExtensionPreviewRenderer(_manifest);
            view.ConfigurePreview(previewRenderer);
            view.InitialViewMode = SplitEditorViewMode.Split;
        }

        // Configure customization script if the manifest declares it
        if (!string.IsNullOrEmpty(_manifest.Customizations))
        {
            var scriptUrl = $"https://{_manifest.HostName}/{_manifest.Customizations}";
            view.CustomizationScriptUrl = scriptUrl;
        }

        return Result<IDocumentView>.Ok(view);
    }
}
