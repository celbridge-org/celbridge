namespace Celbridge.Documents;

/// <summary>
/// The supported types of document view.
/// </summary>
public enum DocumentViewType
{
    /// <summary>
    /// An unsupported document type with an unrecognized file extension.
    /// </summary>
    Unsupported,

    /// <summary>
    /// Text document edited using a text editor control.
    /// e.g. .txt, .cs, .json, .xml, etc.
    /// </summary>
    TextDocument,

    /// <summary>
    /// A web page document with the .web extension.
    /// </summary>
    WebPageDocument,

    /// <summary>
    /// A non-text file resource viewed via a web view.
    /// e.g. an image, audio clip, pdf file, etc.
    /// </summary>
    FileViewer
}
