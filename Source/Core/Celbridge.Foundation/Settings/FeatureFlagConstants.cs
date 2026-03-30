namespace Celbridge.Settings;

/// <summary>
/// Feature flag names used throughout the application.
/// These names must match the keys in appsettings.json and .celbridge files.
/// </summary>
public static class FeatureFlagConstants
{
    /// <summary>
    /// Console panel with IPython REPL terminal.
    /// </summary>
    public const string ConsolePanel = "console-panel";

    /// <summary>
    /// MCP tool system and cel Python API. When disabled, the MCP server does not start
    /// and the Python terminal is not launched.
    /// </summary>
    public const string McpTools = "mcp-tools";

    /// <summary>
    /// Browser developer tools access in WebView-based document editors.
    /// Enabled by default so extension authors can debug their custom editors.
    /// </summary>
    public const string WebViewDevTools = "webview-dev-tools";

}
