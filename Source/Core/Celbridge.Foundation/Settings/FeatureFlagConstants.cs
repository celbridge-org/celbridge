namespace Celbridge.Settings;

/// <summary>
/// Feature flag names used throughout the application.
/// These names must match the keys in appsettings.json and .celbridge files. A flag missing from
/// appsettings.json is off, so each one is listed there with an explicit default.
/// The user-facing titles and descriptions shown on the Project Settings panel live in FeatureFlagCatalog.
/// </summary>
public static class FeatureFlagConstants
{
    /// <summary>
    /// Browser developer tools in WebView-based editors, and the webview_* MCP tools that read and drive
    /// their pages. Enabled by default so extension authors can debug their custom editors.
    /// </summary>
    public const string WebViewDevTools = "webview-dev-tools";

    /// <summary>
    /// Enables the webview_eval MCP tool. This is a separate flag because eval is an
    /// arbitrary code execution primitive, agents without it can use the rest of the
    /// webview_* namespace.
    /// </summary>
    public const string WebViewDevToolsEval = "webview-dev-tools-eval";

    /// <summary>
    /// Enables the app_answer_dialog MCP tool that lets a script answer a
    /// modal dialog without a human present. A test-automation capability,
    /// off by default in shipping builds.
    /// </summary>
    public const string AnswerDialog = "answer-dialog";

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
}
