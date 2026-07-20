using Celbridge.Documents;

namespace Celbridge.Packages;

/// <summary>
/// How a contribution activates when its package is present. Required is always live and cannot be
/// turned off per project; recommended is live by default but a project may disable it; optional ships
/// inert until a project enables it.
/// </summary>
public enum ActivationPolicy
{
    Required,
    Recommended,
    Optional,
}

/// <summary>
/// An editor contributed by a package, parsed from a TOML editor manifest. The package supplies the
/// entire editor UI via an HTML entry point hosted in a WebView, communicating with the host via the
/// IHostDocument JSON-RPC protocol.
/// </summary>
public partial record EditorContribution
{
    /// <summary>
    /// The parent package that provides this contribution.
    /// </summary>
    public PackageInfo Package { get; init; } = new();

    /// <summary>
    /// Unique identifier for this contribution within its package (e.g., "note").
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display name or localization key for this editor.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// The file types this editor handles. Each entry declares the file extension and
    /// an optional display name or localization key for the Add File dialog.
    /// </summary>
    public IReadOnlyList<EditorFileType> FileTypes { get; init; } = [];

    /// <summary>
    /// Optional list of document templates provided by this package.
    /// </summary>
    public IReadOnlyList<DocumentTemplate> Templates { get; init; } = [];

    /// <summary>
    /// Entry point for the editor (e.g., "index.html").
    /// </summary>
    public string EntryPoint { get; init; } = "index.html";

    /// <summary>
    /// Whether this editor handles binary file content.
    /// When true, content is transferred as base64 and saved/loaded as raw bytes.
    /// </summary>
    public bool Binary { get; init; } = false;

    /// <summary>
    /// Whether this editor sources its content from outside the file bytes.
    /// When true, the host returns empty content unless a registered
    /// IDocumentContentProvider matches the resource.
    /// </summary>
    public bool ExternalContent { get; init; } = false;

    /// <summary>
    /// How this contribution activates when its package is present. Required (the default) is always
    /// live with no per-project off switch; recommended is live by default but a project may disable
    /// it; optional ships inert until the project enables it in Project Settings.
    /// </summary>
    public ActivationPolicy Activation { get; init; } = ActivationPolicy.Required;

    /// <summary>
    /// Package-defined options parsed from the [options] table of the editor manifest.
    /// Keys and values are opaque to the host, the editor interprets them.
    /// </summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } = EmptyOptions;

    /// <summary>
    /// Typed configuration keys declared by the manifest's [[config]] entries. Instance tables in
    /// the project config are type-checked against these descriptors.
    /// </summary>
    public IReadOnlyList<ConfigDescriptor> ConfigDescriptors { get; init; } = [];

    /// <summary>
    /// Utility metadata parsed from the [utility] section, or null for an ordinary file-type editor.
    /// </summary>
    public UtilityDescriptor? UtilityDescriptor { get; init; }

    /// <summary>
    /// Whether this contribution is a utility rather than an editor that claims file extensions.
    /// </summary>
    public bool IsUtility => UtilityDescriptor is not null;

    private static readonly IReadOnlyDictionary<string, string> EmptyOptions =
        new Dictionary<string, string>();
}
