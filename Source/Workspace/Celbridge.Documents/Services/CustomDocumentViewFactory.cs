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

    public override DocumentEditorId EditorId => new($"{_contribution.Package.Id}.{_contribution.Id}");

    public override string DisplayName => _resolvedDisplayName;

    public override IReadOnlyList<string> SupportedExtensions =>
        _contribution.FileTypes.Select(ft => ft.FileExtension).ToList();

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
        if (string.IsNullOrEmpty(_contribution.DisplayName))
        {
            return _contribution.Package.Name;
        }

        var localizationStrings = localizationService.LoadStrings(_contribution.Package.PackageFolder);
        if (localizationStrings.TryGetValue(_contribution.DisplayName, out var localizedName))
        {
            return localizedName;
        }

        return _contribution.DisplayName;
    }

    public override bool CanHandleResource(ResourceKey fileResource, string filePath)
    {
        if (!string.IsNullOrEmpty(_contribution.Package.FeatureFlag) &&
            !_featureFlags.IsEnabled(_contribution.Package.FeatureFlag))
        {
            return false;
        }

        return base.CanHandleResource(fileResource, filePath);
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
#if WINDOWS
        var view = _serviceProvider.GetRequiredService<ContributionDocumentView>();
        view.Contribution = _contribution;

        return Result<IDocumentView>.Ok(view);
#else
        return Result<IDocumentView>.Fail("Contribution editors are only available on Windows");
#endif
    }
}
