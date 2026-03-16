using Celbridge.Documents.Views;
using Celbridge.Extensions;
using Celbridge.Settings;

namespace Celbridge.Documents.Services;

/// <summary>
/// Factory for creating ExtensionDocumentView instances for custom (WebView2-based)
/// extension editors. One instance per discovered CustomDocumentContribution.
/// </summary>
public class CustomDocumentViewFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly CustomDocumentContribution _contribution;
    private readonly IFeatureFlags _featureFlags;

    public override IReadOnlyList<string> SupportedExtensions =>
        _contribution.FileTypes.Select(ft => ft.FileExtension).ToList();

    public override EditorPriority Priority => _contribution.Priority;

    public CustomDocumentViewFactory(
        IServiceProvider serviceProvider,
        CustomDocumentContribution contribution,
        IFeatureFlags featureFlags)
    {
        _serviceProvider = serviceProvider;
        _contribution = contribution;
        _featureFlags = featureFlags;
    }

    public override bool CanHandle(ResourceKey fileResource, string filePath)
    {
        if (!string.IsNullOrEmpty(_contribution.Extension.FeatureFlag) &&
            !_featureFlags.IsEnabled(_contribution.Extension.FeatureFlag))
        {
            return false;
        }

        return base.CanHandle(fileResource, filePath);
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
#if WINDOWS
        var view = _serviceProvider.GetRequiredService<ExtensionDocumentView>();
        view.Contribution = _contribution;

        return Result<IDocumentView>.Ok(view);
#else
        return Result<IDocumentView>.Fail("Extension editors are only available on Windows");
#endif
    }
}
