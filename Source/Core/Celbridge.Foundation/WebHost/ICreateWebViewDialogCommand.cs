using Celbridge.Commands;

namespace Celbridge.WebHost;

/// <summary>
/// Display the New Web View Document dialog, then create a .webview document for the given page URL
/// under the name the user accepts.
/// </summary>
public interface ICreateWebViewDialogCommand : IExecutableCommand
{
    /// <summary>
    /// The page URL that becomes the new document's Home URL. The default document name is derived
    /// from its host.
    /// </summary>
    string SourceUrl { get; set; }

    /// <summary>
    /// Resource key for the folder which will contain the new document.
    /// </summary>
    ResourceKey DestFolderResource { get; set; }
}
