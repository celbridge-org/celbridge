using Celbridge.Documents.Views;
using Celbridge.Packages;

namespace Celbridge.Documents.Services;

/// <summary>
/// Factory for creating CustomDocumentView instances for custom (WebView-based)
/// editors. One factory per editor instance.
/// </summary>
public class CustomDocumentViewFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly EditorInstance _instance;
    private readonly string _resolvedDisplayName;
    private readonly IReadOnlyList<string> _supportedFilenames;

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

    public override IReadOnlyList<string> SupportedFilenames => _supportedFilenames;

    public CustomDocumentViewFactory(
        IServiceProvider serviceProvider,
        EditorInstance instance,
        IPackageLocalizationService localizationService)
    {
        _serviceProvider = serviceProvider;
        _instance = instance;
        _resolvedDisplayName = ResolveDisplayName(localizationService);

        // A utility instance owns one backing state file, routed by instance identity. Its
        // factory claims that exact filename rather than a project-wide extension.
        var utilityDescriptor = instance.Contribution.UtilityDescriptor;
        if (utilityDescriptor is not null)
        {
            _supportedFilenames = [$"{instance.InstanceId}{utilityDescriptor.ResourceExtension}"];
        }
        else
        {
            _supportedFilenames = Array.Empty<string>();
        }
    }

    private string ResolveDisplayName(IPackageLocalizationService localizationService)
    {
        // The manifest loader requires every contribution to set display-name, so
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

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        var view = _serviceProvider.GetRequiredService<CustomDocumentView>();
        view.Instance = _instance;
        view.EditorId = EditorId;

        return Result<IDocumentView>.Ok(view);
    }
}
