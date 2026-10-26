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
/// A project file parsed from a [[shortcut]] entry in the .celbridge project config, which opens as a
/// document from a Utility Rail button, when the project loads, or both. Entry order is rail order.
/// </summary>
public sealed record DocumentShortcut
{
    /// <summary>
    /// Resource key of the file the shortcut opens.
    /// </summary>
    public required string Resource { get; init; }

    /// <summary>
    /// Prefixed icon name for the rail button. Empty takes the default shortcut icon.
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// The document area the shortcut opens its document in, which applies only while that document is
    /// not already open. Never the Utility Panel, which holds no document tabs.
    /// </summary>
    public WorkspaceArea Area { get; init; } = WorkspaceArea.Main;

    /// <summary>
    /// True when the shortcut adds a button to the Utility Rail.
    /// </summary>
    public bool HasRailButton { get; init; } = true;

    /// <summary>
    /// True when the document opens each time the project loads, including when it was closed at the end
    /// of the last session.
    /// </summary>
    public bool OpenOnLoad { get; init; }
}

/// <summary>
/// Models the flat keys of the [celbridge] table from the .celbridge project config. The table's
/// sub-tables are modeled separately on ProjectConfig.
/// </summary>
public sealed record class CelbridgeSection
{
    /// <summary>
    /// The version of Celbridge that last opened the project, which migration reads to know what the file
    /// contains.
    /// </summary>
    public string? CelbridgeVersion { get; init; }

    /// <summary>
    /// The project's own version as the file states it, or null when the file sets none or a malformed one.
    /// A missing or empty project version is SemanticVersion.Default.
    /// </summary>
    public string? ProjectVersion { get; init; }

    /// <summary>
    /// A human-readable description of what the project is for. Null when the project sets no description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional map of file extension to editor id: the project's association of a contested
    /// extension with a specific editor. The longest matching suffix applies.
    /// </summary>
    public IReadOnlyDictionary<string, string> EditorAssociations { get; init; } = EmptyEditorAssociations;

    private static readonly IReadOnlyDictionary<string, string> EmptyEditorAssociations =
        new Dictionary<string, string>();
}

/// <summary>
/// Models the [celbridge.explorer] sub-table from the .celbridge project config.
/// </summary>
public sealed record class ExplorerSection
{
    /// <summary>
    /// The Explorer's list: gitignore lines for the Explorer to leave out of its tree. Show Hidden Files
    /// shows the matching resources, dimmed.
    /// </summary>
    public IReadOnlyList<string> Exclude { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Models the [celbridge.search] sub-table from the .celbridge project config.
/// </summary>
public sealed record class SearchSection
{
    /// <summary>
    /// The search list: gitignore lines for search and tag queries to skip.
    /// </summary>
    public IReadOnlyList<string> Exclude { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Models the [celbridge.downloads] sub-table from the .celbridge project config.
/// </summary>
public sealed record class DownloadsSection
{
    /// <summary>
    /// The folder files downloaded from web pages are saved to, as a path from
    /// the project root. Empty saves them to the default downloads folder.
    /// </summary>
    public string Folder { get; init; } = string.Empty;
}

/// <summary>
/// Models the [celbridge.packages] sub-table from the .celbridge project config.
/// </summary>
public sealed record class PackagesSection
{
    /// <summary>
    /// Package folders that the project lists in addition to the packages folder, each a folder path from
    /// the project root. Each direct subfolder of a package folder is a package when it has a package
    /// manifest.
    /// </summary>
    public IReadOnlyList<string> Folders { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Names of the packages that the project has turned off. A discovered package not listed here
    /// contributes its default-active contributions. A listed package contributes nothing.
    /// </summary>
    public IReadOnlyList<string> Disabled { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Root Celbridge project config, parsed from the .celbridge file's v2 schema.
/// </summary>
public sealed record class ProjectConfig
{
    /// <summary>
    /// The flat keys of the [celbridge] table: versions, the description and editor defaults.
    /// </summary>
    public CelbridgeSection Celbridge { get; init; } = new();

    /// <summary>
    /// The Explorer's list from the [celbridge.explorer] sub-table.
    /// </summary>
    public ExplorerSection Explorer { get; init; } = new();

    /// <summary>
    /// The search list from the [celbridge.search] sub-table.
    /// </summary>
    public SearchSection Search { get; init; } = new();

    /// <summary>
    /// The downloads folder from the [celbridge.downloads] sub-table.
    /// </summary>
    public DownloadsSection Downloads { get; init; } = new();

    /// <summary>
    /// Package folders and package activation from the [celbridge.packages] sub-table.
    /// </summary>
    public PackagesSection Packages { get; init; } = new();

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
    /// Document shortcuts, from the [[shortcut]] entries, in the order the rail shows them.
    /// </summary>
    public IReadOnlyList<DocumentShortcut> DocumentShortcuts { get; init; } = Array.Empty<DocumentShortcut>();

    /// <summary>
    /// Entries that were skipped or degraded during parsing.
    /// </summary>
    public IReadOnlyList<ProjectConfigEntryError> EntryErrors { get; init; } = Array.Empty<ProjectConfigEntryError>();
}
