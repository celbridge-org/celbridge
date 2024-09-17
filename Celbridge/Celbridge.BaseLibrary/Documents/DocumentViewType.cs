namespace Celbridge.Documents;

/// <summary>
/// The supported types of document view.
/// </summary>
public enum DocumentViewType
{
    /// <summary>
    /// An unsupported document type using an unrecognized extension.
    /// </summary>
    Unsupported,

    /// <summary>
    /// Text document editor using a text editor control.
    /// </summary>
    TextDocument,

    /// <summary>
    /// View a web page document.
    /// </summary>
    WebPageDocument,

    /// <summary>
    /// View a non-text file resource via a web view (e.g. an image, audio clip or pdf file).
    /// </summary>
    FileViewer
}
