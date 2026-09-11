namespace Celbridge.Settings;

/// <summary>
/// Feature flag names used throughout the application.
/// These names must match the keys in appsettings.json and .celbridge files.
/// The user-facing titles and descriptions shown on the Project Settings panel live in FeatureFlagCatalog.
/// </summary>
public static class FeatureFlagConstants
{
    /// <summary>
    /// MCP tool system and cel Python API. When disabled, the MCP server does not start
    /// and the Python terminal launches without the cel proxy.
    /// </summary>
    public const string McpTools = "mcp-tools";

    /// <summary>
    /// Browser developer tools access in WebView-based document editors.
    /// Enabled by default so extension authors can debug their custom editors.
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
    /// Enables the built-in WebFetch and WebSearch tools for coding agents.
    /// </summary>
    public const string WebAccessTools = "web-access-tools";

    /// <summary>
    /// Narrates every navigation and attach of a hosted page into the log. A page that loads blank is
    /// reported whether or not this is enabled; what it adds is the surrounding timeline.
    /// </summary>
    public const string WebViewLoadDiagnostics = "webview-load-diagnostics";

    /// <summary>
    /// The workshop: the package_* and page_* tools, and the Workshop section of Application Settings.
    /// An experimental feature that needs a configured workshop server, so the build opts in rather than
    /// the user. A build-time flag.
    /// </summary>
    public const string Workshop = "workshop";

    /// <summary>
    /// The flags fixed for the lifetime of the build. They are read from appsettings.json only: a project
    /// override is ignored, and they are absent from the Feature Flags section, because the surfaces they
    /// gate exist before any project is loaded and so cannot follow a project's choice. Unlike a runtime
    /// flag, one of these is off unless the build turns it on.
    /// </summary>
    public static readonly IReadOnlySet<string> BuildTimeFlags = new HashSet<string>(StringComparer.Ordinal)
    {
        Workshop
    };
}
