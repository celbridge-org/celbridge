namespace Celbridge.Explorer;

/// <summary>
/// File formats that can be added as resources via the explorer window.
/// </summary>
public enum ResourceFormat
{
    /// <summary>
    /// A file "format" corresponding to a folder resource.
    /// </summary>
    Folder,

    /// <summary>
    /// Any text file format (e.g. .txt, .cs, .json, .xml, etc.)
    /// </summary>
    Text,

    /// <summary>
    /// An Excel spreadsheet document with the .xlsx extension
    /// </summary>
    Excel,

    /// <summary>
    /// A markdown document with the .md extension
    /// </summary>
    Markdown,

    /// <summary>
    /// A Python script file with the .py extension
    /// </summary>
    Python,

    /// <summary>
    /// A web app document with the .webapp extension
    /// </summary>
    WebApp
}
