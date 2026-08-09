using Celbridge.Packages;

namespace Celbridge.DocumentEditors;

/// <summary>
/// Registers the document-editor packages bundled with this module. They are served over the loopback file
/// server and driven over the WebSocket host channel, so they run on every head.
/// </summary>
public sealed class DocumentEditorsBundledPackageProvider : IBundledPackageProvider
{
    private const string EditorsFolderName = "Editors";

    public IReadOnlyList<BundledPackageDescriptor> GetBundledPackages()
    {
        var editorsRoot = Path.Combine(AppContext.BaseDirectory, "Celbridge.DocumentEditors", EditorsFolderName);

        return new[]
        {
            new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "Notes") },
            new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "FileViewer") },
            new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "CodeEditor") },
            new BundledPackageDescriptor { Folder = Path.Combine(editorsRoot, "UtilityDemo") },
        };
    }
}
