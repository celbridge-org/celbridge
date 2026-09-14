namespace Celbridge.Settings;

/// <summary>
/// Feature flag names used throughout the application.
/// These names must match the keys in appsettings.json and .celbridge files.
/// The user-facing titles and descriptions shown on the Project Settings panel live in FeatureFlagCatalog.
/// </summary>
public static class FeatureFlagConstants
{
    /// <summary>
    /// Browser developer tools in WebView-based editors, and the webview_* MCP tools that read and drive
    /// their pages. Enabled by default.
    /// </summary>
    public const string WebViewDevTools = "webview-dev-tools";

    /// <summary>
    /// Enables the webview_eval MCP tool. This is a separate flag because eval is an
    /// arbitrary code execution primitive, agents without it can use the rest of the
    /// webview_* namespace.
    /// </summary>
    public const string WebViewDevToolsEval = "webview-dev-tools-eval";

    /// <summary>
    /// Narrates every navigation and attach of a hosted page into the log. A page that loads blank is
    /// reported whether or not this is enabled; what it adds is the surrounding timeline.
    /// </summary>
    public const string WebViewLoadDiagnostics = "webview-load-diagnostics";

    /// <summary>
    /// Shows the Open metadata file item on the Explorer context menu, which opens a file's .cel sidecar in
    /// the code editor for editing by hand.
    /// </summary>
    public const string OpenCel = "open-cel";

    /// <summary>
    /// Registers the bundled Notes editor for .note files, with its New File template. Off by default.
    /// </summary>
    public const string NoteEditor = "note-editor";
}
