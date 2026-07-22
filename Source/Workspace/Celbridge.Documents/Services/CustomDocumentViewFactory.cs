using Celbridge.Documents.Views;
using Celbridge.Packages;
using Celbridge.UserInterface.Helpers;

namespace Celbridge.Documents.Services;

/// <summary>
/// Factory for creating CustomDocumentView instances for custom (WebView-based)
/// editors. One factory per resolved editor.
/// </summary>
public class CustomDocumentViewFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ResolvedEditor _resolvedEditor;
    private readonly string _resolvedDisplayName;
    private readonly string _resolvedDescription;
    private readonly IReadOnlyList<string> _supportedFilenames;

    public override EditorId EditorId => _resolvedEditor.EditorId;

    public override string DisplayName => _resolvedDisplayName;

    /// <summary>
    /// The localized editor description, used as the docked tab tooltip. Empty when the manifest
    /// declares no description.
    /// </summary>
    public string Description => _resolvedDescription;

    /// <summary>
    /// The resolved editor this factory was built from.
    /// </summary>
    public ResolvedEditor ResolvedEditor => _resolvedEditor;

    public EditorContribution Contribution => _resolvedEditor.Contribution;

    public override bool IsUtility => _resolvedEditor.Contribution.IsUtility;

    public override IReadOnlyList<string> SupportedExtensions =>
        _resolvedEditor.Contribution.FileTypes.Select(fileType => fileType.FileExtension).ToList();

    public override IReadOnlyList<string> SupportedFilenames => _supportedFilenames;

    public CustomDocumentViewFactory(
        IServiceProvider serviceProvider,
        ResolvedEditor resolvedEditor,
        IPackageLocalizationService localizationService)
    {
        _serviceProvider = serviceProvider;
        _resolvedEditor = resolvedEditor;
        _resolvedDisplayName = PackageDisplayText.Resolve(localizationService, resolvedEditor.Contribution.Package, resolvedEditor.Contribution.DisplayName);
        _resolvedDescription = PackageDisplayText.Resolve(localizationService, resolvedEditor.Contribution.Package, resolvedEditor.Contribution.Description);

        // A utility owns one backing state file, routed by editor id. Its
        // factory claims that exact filename rather than a project-wide extension.
        var utilityDescriptor = resolvedEditor.Contribution.UtilityDescriptor;
        if (utilityDescriptor is not null)
        {
            _supportedFilenames = [$"{resolvedEditor.EditorId}{utilityDescriptor.ResourceExtension}"];
        }
        else
        {
            _supportedFilenames = Array.Empty<string>();
        }
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        var view = _serviceProvider.GetRequiredService<CustomDocumentView>();
        view.ResolvedEditor = _resolvedEditor;
        view.EditorId = EditorId;

        return Result<IDocumentView>.Ok(view);
    }
}
