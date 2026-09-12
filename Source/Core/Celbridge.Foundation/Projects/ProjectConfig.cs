using Celbridge.Workspace;

namespace Celbridge.Projects;

/// <summary>
/// An entry in the .celbridge project config that was skipped or degraded during parsing,
/// with the entry name and the reason. The rest of the file still applies.
/// </summary>
public record ProjectConfigEntryError(string EntryName, string Message);

/// <summary>
/// A per-contribution override parsed from a [[contribution]] entry in the .celbridge project
/// config: the contribution it targets, an optional activation flip, and any non-default config
/// values. A contribution running at its manifest default with default config has no entry.
/// </summary>
public sealed record ContributionOverride
{
    /// <summary>
    /// Name of the package that provides the editor.
    /// </summary>
    public required string PackageName { get; init; }

    /// <summary>
    /// Contribution id of the editor within its package.
    /// </summary>
    public required string ContributionId { get; init; }

    /// <summary>
    /// True when a default-active contribution is turned off, persisted as disabled = true. Ignored
    /// on an optional contribution, which is off unless Enabled.
    /// </summary>
    public bool Disabled { get; init; }

    /// <summary>
    /// True when an optional contribution is turned on, persisted as enabled = true. Ignored on a
    /// default-active contribution, which is on unless Disabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The contribution's configuration: every non-reserved key on the entry, holding the raw TOML
    /// value (string, bool, long, double, or IReadOnlyList of string). Type-checked against the
    /// editor's config descriptors when the workspace loads.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Config { get; init; } = EmptyConfig;

    private static readonly IReadOnlyDictionary<string, object?> EmptyConfig =
        new Dictionary<string, object?>();
}

/// <summary>
/// A Utility Rail button parsed from a [[shortcut]] entry in the .celbridge project config: the resource
/// the button opens as a document, and the icon it carries. Entry order is rail order.
/// </summary>
public sealed record DocumentShortcut
{
    /// <summary>
    /// Resource key of the file the shortcut opens.
    /// </summary>
    public required string Resource { get; init; }

    /// <summary>
    /// Prefixed icon name for the rail button. Empty takes the default document icon.
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// The document area the shortcut opens its document in, which applies only while that document is
    /// not already open. Never the Utility Panel, which holds no document tabs.
    /// </summary>
    public WorkspaceArea Area { get; init; } = WorkspaceArea.Main;
}

/// <summary>
/// Models the [celbridge] table from the .celbridge project config: every host-level declaration
/// as flat keys, plus the [celbridge.resources] sub-table modeled separately on ProjectConfig.
/// </summary>
public sealed record class CelbridgeSection
{
    /// <summary>
    /// Schema version of the project config, driving versioned migrations.
    /// </summary>
    public string? CelbridgeVersion { get; init; }

    /// <summary>
    /// The project's own version.
    /// </summary>
    public string? ProjectVersion { get; init; }

    /// <summary>
    /// A human-readable description of what the project is for. Null when the project sets no description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Name of the folder under .celbridge/ holding this project's data. Empty, the default, puts that
    /// data at the root of .celbridge/. A single relative path segment; anything else is rejected on
    /// parse, because it builds filesystem paths inside a folder the resource layer reserves.
    /// </summary>
    public string DataFolder { get; init; } = string.Empty;

    /// <summary>
    /// Package names the project has turned off. A discovered package not listed here contributes its
    /// default-active contributions. A listed package contributes nothing.
    /// </summary>
    public IReadOnlyList<string> DisabledPackages { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional map of file extension to editor id: the project's association of a contested
    /// extension with a specific editor. The longest matching suffix applies.
    /// </summary>
    public IReadOnlyDictionary<string, string> EditorAssociations { get; init; } = EmptyEditorAssociations;

    private static readonly IReadOnlyDictionary<string, string> EmptyEditorAssociations =
        new Dictionary<string, string>();
}

/// <summary>
/// Models the [celbridge.resources] sub-table from the .celbridge project config.
/// The two keys bind different audiences: Hide changes what the Explorer draws,
/// SearchExclude changes what the indexers walk. Neither grants or withholds access.
/// </summary>
public sealed record class ResourcesSection
{
    /// <summary>
    /// Patterns matching resources the Explorer leaves out of the tree unless
    /// Show Hidden Files is on. A hidden resource stays readable and writable.
    /// </summary>
    public IReadOnlyList<string> Hide { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Patterns matching resources that search, reference scanning and tag
    /// queries skip. Until the resource index lands these are left out of the
    /// project tree as well, so the Explorer does not draw them. An excluded
    /// resource stays readable and writable.
    /// </summary>
    public IReadOnlyList<string> SearchExclude { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Root Celbridge project config, parsed from the .celbridge file's v2 schema.
/// </summary>
public sealed record class ProjectConfig
{
    /// <summary>
    /// The [celbridge] table: versions, package activation, and editor defaults.
    /// </summary>
    public CelbridgeSection Celbridge { get; init; } = new();

    /// <summary>
    /// File policy from the [celbridge.resources] sub-table.
    /// </summary>
    public ResourcesSection Resources { get; init; } = new();

    /// <summary>
    /// Project feature-flag overrides from the [celbridge].features inline table.
    /// </summary>
    public IReadOnlyDictionary<string, bool> Features { get; init; } = new Dictionary<string, bool>();

    /// <summary>
    /// Per-contribution overrides of the discovered defaults, from the [[contribution]] entries.
    /// A contribution running at its manifest default with default config has no entry.
    /// </summary>
    public IReadOnlyList<ContributionOverride> ContributionOverrides { get; init; } = Array.Empty<ContributionOverride>();

    /// <summary>
    /// Utility Rail document shortcuts, from the [[shortcut]] entries, in the order the rail shows them.
    /// </summary>
    public IReadOnlyList<DocumentShortcut> DocumentShortcuts { get; init; } = Array.Empty<DocumentShortcut>();

    /// <summary>
    /// Entries that were skipped or degraded during parsing.
    /// </summary>
    public IReadOnlyList<ProjectConfigEntryError> EntryErrors { get; init; } = Array.Empty<ProjectConfigEntryError>();
}
