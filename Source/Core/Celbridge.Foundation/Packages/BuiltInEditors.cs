using Celbridge.Documents;

namespace Celbridge.Packages;

/// <summary>
/// A bundled contribution the host always activates, registered under a host-assigned editor id.
/// An optional built-in ships in the installer but may be absent from a source build whose private
/// libraries are not available, in which case its editor is simply not offered.
/// </summary>
public record BuiltInEditorDefinition(
    EditorInstanceId EditorId,
    string PackageName,
    string ContributionId,
    bool Optional = false);

/// <summary>
/// Catalog of the built-in editors present in every project. Built-ins need no activation entry and
/// no instance declaration; they carry host-assigned dotted ids so the registry's key space holds
/// them beside the dot-free project-declared instance ids.
/// </summary>
public static class BuiltInEditors
{
    /// <summary>
    /// Built-in id of the Monaco code editor.
    /// </summary>
    public static readonly EditorInstanceId CodeEditorId = new("celbridge.code");

    /// <summary>
    /// Built-in id of the Markdown editor.
    /// </summary>
    public static readonly EditorInstanceId MarkdownEditorId = new("celbridge.markdown");

    /// <summary>
    /// Built-in id of the File Viewer.
    /// </summary>
    public static readonly EditorInstanceId FileViewerId = new("celbridge.file-viewer");

    /// <summary>
    /// Built-in id of the spreadsheet editor. Present in installer builds; absent from a source
    /// build without the SpreadJS library.
    /// </summary>
    public static readonly EditorInstanceId SpreadsheetEditorId = new("celbridge.spreadsheet");

    /// <summary>
    /// Built-in id of the web view editor, registered natively by the WebView module.
    /// </summary>
    public static readonly EditorInstanceId WebViewEditorId = new("celbridge.webview-editor");

    /// <summary>
    /// Built-in id of the HTML viewer, registered natively by the WebView module.
    /// </summary>
    public static readonly EditorInstanceId HtmlViewerId = new("celbridge.html-viewer");

    /// <summary>
    /// Bundled packages the host always activates, independent of the project's activation list.
    /// </summary>
    public static readonly IReadOnlyList<string> AlwaysActivePackages =
    [
        "celbridge.code-editor",
        "celbridge.file-viewer",
        "celbridge.spreadsheet",
    ];

    /// <summary>
    /// The package built-ins: bundled contributions registered under host-assigned ids.
    /// </summary>
    public static readonly IReadOnlyList<BuiltInEditorDefinition> PackageBuiltIns =
    [
        new BuiltInEditorDefinition(MarkdownEditorId, "celbridge.code-editor", "markdown"),
        new BuiltInEditorDefinition(FileViewerId, "celbridge.file-viewer", "file-viewer"),
        new BuiltInEditorDefinition(SpreadsheetEditorId, "celbridge.spreadsheet", "spreadsheet", Optional: true),
        new BuiltInEditorDefinition(CodeEditorId, "celbridge.code-editor", "code"),
    ];

    /// <summary>
    /// Fixed resolution order for built-in editors, applied after every declared instance. Pinned
    /// to preserve the pre-instance defaults: specialized editors ahead of the general code editor.
    /// </summary>
    public static readonly IReadOnlyList<EditorInstanceId> HostResolutionOrder =
    [
        MarkdownEditorId,
        HtmlViewerId,
        WebViewEditorId,
        SpreadsheetEditorId,
        FileViewerId,
        CodeEditorId,
    ];

    /// <summary>
    /// Returns true if the package is a built-in package that the host always activates.
    /// </summary>
    public static bool IsAlwaysActivePackage(string packageName)
    {
        return AlwaysActivePackages.Contains(packageName, StringComparer.Ordinal);
    }
}
