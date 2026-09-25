using Celbridge.Documents;

namespace Celbridge.Packages;

/// <summary>
/// A bundled contribution the host always activates, registered under a host-assigned editor id.
/// An optional built-in ships in the installer but may be absent from a source build whose private
/// libraries are not available, in which case its editor is simply not offered.
/// </summary>
public record BuiltInEditorDefinition(
    EditorId EditorId,
    string PackageName,
    string ContributionId,
    bool Optional = false);

/// <summary>
/// Catalog of the built-in editors present in every project. Built-ins need no activation entry and
/// no contribution declaration; they carry host-assigned dotted ids so the registry's key space holds
/// them beside the project-declared editor ids.
/// </summary>
public static class BuiltInEditors
{
    /// <summary>
    /// Built-in id of the Monaco code editor.
    /// </summary>
    public static readonly EditorId CodeEditorId = new("celbridge.code");

    /// <summary>
    /// Built-in id of the Markdown editor.
    /// </summary>
    public static readonly EditorId MarkdownEditorId = new("celbridge.markdown");

    /// <summary>
    /// Built-in id of the HTML editor, which edits a page's source beside a live preview of the page.
    /// </summary>
    public static readonly EditorId HtmlEditorId = new("celbridge.html");

    /// <summary>
    /// Built-in id of the File Viewer.
    /// </summary>
    public static readonly EditorId FileViewerId = new("celbridge.file-viewer");

    /// <summary>
    /// Built-in id of the spreadsheet editor. Present in installer builds; absent from a source
    /// build without the SpreadJS library.
    /// </summary>
    public static readonly EditorId SpreadsheetEditorId = new("celbridge.spreadsheet");

    /// <summary>
    /// Built-in id of the Report Viewer.
    /// </summary>
    public static readonly EditorId ReportViewerId = new("celbridge.report");

    /// <summary>
    /// Built-in id of the web view editor, registered natively by the WebView module.
    /// </summary>
    public static readonly EditorId WebViewEditorId = new("celbridge.webview-editor");

    /// <summary>
    /// Built-in id of the Project Settings editor, registered natively by the Project Settings module.
    /// Absent from BuiltInResolutionOrder because it reserves the project file type, and a reserving
    /// editor holds its file types ahead of the pinned order rather than taking a place in it.
    /// </summary>
    public static readonly EditorId ProjectSettingsEditorId = new("celbridge.project-settings");

    /// <summary>
    /// The package built-ins: bundled contributions registered under host-assigned ids. Ordered to
    /// match the shared editors' relative order in BuiltInResolutionOrder, which is the authority for
    /// open precedence; the two lists differ only in that BuiltInResolutionOrder also carries the
    /// natively registered WebView editor, which is not a package contribution.
    /// </summary>
    public static readonly IReadOnlyList<BuiltInEditorDefinition> PackageBuiltIns =
    [
        new BuiltInEditorDefinition(MarkdownEditorId, "celbridge-code-editor", "markdown"),
        new BuiltInEditorDefinition(HtmlEditorId, "celbridge-code-editor", "html"),
        new BuiltInEditorDefinition(SpreadsheetEditorId, "celbridge-spreadsheet", "spreadsheet", Optional: true),
        new BuiltInEditorDefinition(ReportViewerId, "celbridge-report", "report"),
        new BuiltInEditorDefinition(FileViewerId, "celbridge-file-viewer", "file-viewer"),
        new BuiltInEditorDefinition(CodeEditorId, "celbridge-code-editor", "code"),
    ];

    /// <summary>
    /// Fixed resolution order for built-in editors, applied after every declared editor. Specialized
    /// editors rank ahead of the general code editor. This
    /// is the authority for built-in open precedence; PackageBuiltIns lists the same contributions
    /// (minus the natively registered WebView editor) in the same relative order.
    /// </summary>
    public static readonly IReadOnlyList<EditorId> BuiltInResolutionOrder =
    [
        MarkdownEditorId,
        HtmlEditorId,
        WebViewEditorId,
        SpreadsheetEditorId,
        ReportViewerId,
        FileViewerId,
        CodeEditorId,
    ];

    /// <summary>
    /// Returns true if the package contributes a built-in editor, which makes it a built-in package
    /// that the host always activates.
    /// </summary>
    public static bool IsAlwaysActivePackage(string packageName)
    {
        return PackageBuiltIns.Any(definition => definition.PackageName.Equals(packageName, StringComparison.Ordinal));
    }
}
