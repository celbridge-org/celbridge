using Celbridge.Documents.Views;
using Celbridge.Packages;
using Celbridge.Settings;

namespace Celbridge.Documents.Services;

/// <summary>
/// Factory for creating ContributionDocumentView instances for custom (WebView-based)
/// extension editors. One instance per discovered CustomDocumentEditorContribution.
/// </summary>
public class CustomDocumentViewFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly CustomDocumentEditorContribution _contribution;
    private readonly IFeatureFlags _featureFlags;
    private readonly string _resolvedDisplayName;

    public override DocumentEditorId EditorId => new($"{_contribution.Package.Name}.{_contribution.Id}");

    public override string DisplayName => _resolvedDisplayName;

    /// <summary>
    /// The contribution this factory was built from. Exposed so the documents panel and the utility
    /// seeder can reach the utility metadata (glyph, tooltip, template) when a utility document is opened.
    /// </summary>
    public CustomDocumentEditorContribution Contribution => _contribution;

    public override bool IsUtility => _contribution.IsUtility;

    public override IReadOnlyList<string> SupportedExtensions =>
        _contribution.FileTypes.Select(fileType => fileType.FileExtension).ToList();

    public override EditorPriority Priority => _contribution.Priority;

    public CustomDocumentViewFactory(
        IServiceProvider serviceProvider,
        CustomDocumentEditorContribution contribution,
        IFeatureFlags featureFlags,
        IPackageLocalizationService localizationService)
    {
        _serviceProvider = serviceProvider;
        _contribution = contribution;
        _featureFlags = featureFlags;
        _resolvedDisplayName = ResolveDisplayName(localizationService);
    }

    private string ResolveDisplayName(IPackageLocalizationService localizationService)
    {
        // The manifest loader requires every document contribution to set
        // display_name, so _contribution.DisplayName is guaranteed non-empty
        // here. The value may be a localization key or a plain string. Run it
        // through the package's localization dictionary and return the raw
        // value when the key is not present (which also handles plain strings).
        var displayKey = _contribution.DisplayName;

        var localizationStrings = localizationService.LoadStrings(_contribution.Package);
        if (localizationStrings.TryGetValue(displayKey, out var localizedName))
        {
            return localizedName;
        }

        return displayKey;
    }

    public override bool CanHandleResource(ResourceKey fileResource)
    {
        if (!string.IsNullOrEmpty(_contribution.Package.FeatureFlag) &&
            !_featureFlags.IsEnabled(_contribution.Package.FeatureFlag))
        {
            return false;
        }

        return base.CanHandleResource(fileResource);
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        var view = _serviceProvider.GetRequiredService<ContributionDocumentView>();
        view.Contribution = _contribution;
        view.EditorId = EditorId;

        return Result<IDocumentView>.Ok(view);
    }
}
