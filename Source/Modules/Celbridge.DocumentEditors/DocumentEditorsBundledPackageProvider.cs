using Celbridge.Packages;
using Celbridge.Settings;

namespace Celbridge.DocumentEditors;

/// <summary>
/// Registers the document-editor packages bundled with this module. They are served over the loopback file
/// server and driven over the WebSocket host channel, so they run on every head. The Notes editor package is
/// registered only while the note-editor feature flag is on.
/// </summary>
public sealed class DocumentEditorsBundledPackageProvider : IBundledPackageProvider
{
    private const string EditorsFolderName = "Editors";

    private readonly IFeatureFlags _featureFlags;

    public DocumentEditorsBundledPackageProvider(IFeatureFlags featureFlags)
    {
        _featureFlags = featureFlags;
    }

    public IReadOnlyList<BundledPackageDescriptor> GetBundledPackages()
    {
        var editorsRoot = Path.Combine(AppContext.BaseDirectory, "Celbridge.DocumentEditors", EditorsFolderName);

        var packages = new List<BundledPackageDescriptor>();

        // Bundled packages are discovered on every workspace load, after the project's feature flag overrides
        // are applied, so each project decides whether it has the Notes editor.
        if (_featureFlags.IsEnabled(FeatureFlagConstants.NoteEditor))
        {
            packages.Add(new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "Notes") });
        }

        packages.Add(new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "FileViewer") });
        packages.Add(new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "CodeEditor") });
        packages.Add(new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "Report") });
        packages.Add(new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "UtilityDemo") });

        return packages;
    }
}
