namespace Celbridge.Settings;

/// <summary>
/// A feature flag. Name is the name in appsettings.json and project files. Agent tools report the flag by the same
/// name. TitleKey and DescriptionKey are the resource keys of the title and description in the Features section of
/// Project Settings.
/// </summary>
public sealed record FeatureFlagDescriptor(string Name, string TitleKey, string DescriptionKey);

/// <summary>
/// A group of related feature flags, shown as one card in the Features section. TitleKey is the resource key of the
/// group's localized title, and Flags lists the group's flags in display order.
/// </summary>
public sealed record FeatureFlagGroupDescriptor(string TitleKey, IReadOnlyList<FeatureFlagDescriptor> Flags);

/// <summary>
/// The catalog of every feature flag, declared once. Code reads a flag through IFeatureFlags with its descriptor
/// here. The groups list every flag, by area and in display order.
/// </summary>
public static class FeatureFlagCatalog
{
    /// <summary>
    /// Browser developer tools in WebView-based editors, and the webview_* MCP tools that read and drive
    /// their pages. Enabled by default.
    /// </summary>
    public static readonly FeatureFlagDescriptor WebViewDevTools = new(
        "webview-dev-tools",
        "ProjectSettings_FeatureFlag_WebViewDevTools_Title",
        "ProjectSettings_FeatureFlag_WebViewDevTools_Description");

    /// <summary>
    /// Enables the webview_eval MCP tool. The flag is separate because eval can run
    /// arbitrary code. Agents without this flag can still use the rest of the
    /// webview_* namespace.
    /// </summary>
    public static readonly FeatureFlagDescriptor WebViewDevToolsEval = new(
        "webview-dev-tools-eval",
        "ProjectSettings_FeatureFlag_WebViewDevToolsEval_Title",
        "ProjectSettings_FeatureFlag_WebViewDevToolsEval_Description");

    /// <summary>
    /// Narrates every navigation and attach of a hosted page into the log. A page that loads blank is
    /// reported whether or not this is enabled. The flag adds the surrounding timeline.
    /// </summary>
    public static readonly FeatureFlagDescriptor WebViewLoadDiagnostics = new(
        "webview-load-diagnostics",
        "ProjectSettings_FeatureFlag_WebViewLoadDiagnostics_Title",
        "ProjectSettings_FeatureFlag_WebViewLoadDiagnostics_Description");

    /// <summary>
    /// Registers the bundled Notes editor for .note files, with its New File template. Off by default.
    /// </summary>
    public static readonly FeatureFlagDescriptor NoteEditor = new(
        "note-editor",
        "ProjectSettings_FeatureFlag_NoteEditor_Title",
        "ProjectSettings_FeatureFlag_NoteEditor_Description");

    /// <summary>
    /// Python consoles and scripts, and the code editor's language server for Python. The project load installs uv,
    /// the celbridge wheel and basedpyright for a project with the feature on.
    /// </summary>
    public static readonly FeatureFlagDescriptor Python = new(
        "python",
        "ProjectSettings_FeatureFlag_Python_Title",
        "ProjectSettings_FeatureFlag_Python_Description");

    /// <summary>
    /// Code editor support for TypeScript and JavaScript files, from the TypeScript language server. Loading a
    /// project with the feature on installs TypeScript.
    /// </summary>
    public static readonly FeatureFlagDescriptor TypeScript = new(
        "typescript",
        "ProjectSettings_FeatureFlag_TypeScript_Title",
        "ProjectSettings_FeatureFlag_TypeScript_Description");

    /// <summary>
    /// The groups of the Features section, in display order. Every flag above belongs to one group.
    /// </summary>
    public static readonly IReadOnlyList<FeatureFlagGroupDescriptor> Groups = new List<FeatureFlagGroupDescriptor>
    {
        new("ProjectSettings_FeatureFlagGroup_Web_Title", new List<FeatureFlagDescriptor>
        {
            WebViewDevTools,
            WebViewDevToolsEval,
            WebViewLoadDiagnostics,
        }),
        new("ProjectSettings_FeatureFlagGroup_Documents_Title", new List<FeatureFlagDescriptor>
        {
            NoteEditor,
        }),
        new("ProjectSettings_FeatureFlagGroup_Languages_Title", new List<FeatureFlagDescriptor>
        {
            Python,
            TypeScript,
        }),
    };
}
