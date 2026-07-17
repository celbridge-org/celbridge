using Celbridge.Documents.Views;
using Celbridge.Packages;
using Celbridge.Settings;

namespace Celbridge.Documents.Services;

/// <summary>
/// Factory for creating CustomDocumentView instances for custom (WebView-based)
/// editors. One factory per editor instance.
/// </summary>
public class CustomDocumentViewFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly EditorInstance _instance;
    private readonly IFeatureFlags _featureFlags;
    private readonly string _resolvedDisplayName;

    public override EditorInstanceId EditorId => _instance.InstanceId;

    public override string DisplayName => _resolvedDisplayName;

    /// <summary>
    /// The editor instance this factory was built from.
    /// </summary>
    public EditorInstance Instance => _instance;

    public EditorContribution Contribution => _instance.Contribution;

    public override bool IsUtility => _instance.Contribution.IsUtility;

    public override IReadOnlyList<string> SupportedExtensions =>
        _instance.Contribution.FileTypes.Select(fileType => fileType.FileExtension).ToList();

    public override EditorPriority Priority => _instance.Contribution.Priority;

    public CustomDocumentViewFactory(
        IServiceProvider serviceProvider,
        EditorInstance instance,
        IFeatureFlags featureFlags,
        IPackageLocalizationService localizationService)
    {
        _serviceProvider = serviceProvider;
        _instance = instance;
        _featureFlags = featureFlags;
        _resolvedDisplayName = ResolveDisplayName(localizationService);
    }

    private string ResolveDisplayName(IPackageLocalizationService localizationService)
    {
        // The manifest loader requires every contribution to set display_name, so
        // DisplayName is guaranteed non-empty here. The value may be a localization
        // key or a plain string.
        var displayKey = _instance.Contribution.DisplayName;

        var localizationStrings = localizationService.LoadStrings(_instance.Contribution.Package);
        if (localizationStrings.TryGetValue(displayKey, out var localizedName))
        {
            return localizedName;
        }

        return displayKey;
    }

    public override bool CanHandleResource(ResourceKey fileResource)
    {
        var featureFlag = _instance.Contribution.Package.FeatureFlag;
        if (!string.IsNullOrEmpty(featureFlag) &&
            !_featureFlags.IsEnabled(featureFlag))
        {
            return false;
        }

        return base.CanHandleResource(fileResource);
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        var view = _serviceProvider.GetRequiredService<CustomDocumentView>();
        view.Contribution = _instance.Contribution;
        view.EditorId = EditorId;

        return Result<IDocumentView>.Ok(view);
    }
}
