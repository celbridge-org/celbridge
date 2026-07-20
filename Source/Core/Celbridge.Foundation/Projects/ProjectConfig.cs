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
    /// Package names the project has turned off. A discovered package not listed here contributes its
    /// default-active contributions; a listed package contributes nothing. Activation is otherwise
    /// discovery-driven, so this records opt-outs rather than opt-ins.
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
/// Inputs to the workspace-scoped policy engine. The resource set is computed
/// as (not ignored by the ignore-file, or matched by Add) and not matched by
/// Remove. Lock is a separate axis that freezes paths in place.
/// </summary>
public sealed record class ResourcesSection
{
    /// <summary>
    /// Path to a gitignore-format file (relative to the project root) whose
    /// matched files are excluded from the resource set. Defaults to ".gitignore".
    /// An empty string disables the ignore baseline, so every on-disk path below
    /// the system tier is a candidate resource.
    /// </summary>
    public string IgnoreFile { get; init; } = ".gitignore";

    /// <summary>
    /// Patterns that add resources back into the set even when the ignore-file
    /// hides them (e.g. "Python/.venv/**"). Empty by default.
    /// </summary>
    public IReadOnlyList<string> Add { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Patterns that drop resources from the set entirely. Takes precedence over
    /// Add and the ignore baseline. Empty by default.
    /// </summary>
    public IReadOnlyList<string> Remove { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Patterns matching resources frozen in place: content cannot be written
    /// and neither the resource nor any ancestor folder can be moved, renamed,
    /// or deleted. Applies to every caller, including the in-app editor.
    /// </summary>
    public IReadOnlyList<string> Lock { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Models the interim [project] table from the .celbridge project config, carrying only the
/// Python environment keys until they move to console instance config. Uses pyproject.toml
/// naming conventions so the section can be copied to a pyproject.toml file.
/// </summary>
public sealed record class ProjectSection
{
    /// <summary>
    /// Python version requirement (e.g., ">=3.12").
    /// </summary>
    public string? RequiresPython { get; init; }

    /// <summary>
    /// List of Python package dependencies to install in the environment.
    /// </summary>
    public IReadOnlyList<string>? Dependencies { get; init; }
}

/// <summary>
/// Represents a validation error found during shortcut configuration parsing.
/// </summary>
public record ShortcutValidationError(int ShortcutIndex, string PropertyName, string Message);

/// <summary>
/// Definition of a shortcut from the [[shortcut]] array.
/// The name property contains the full hierarchical path using "/" as separator.
/// Example: "Run Examples/Hello World" creates a "Hello World" item under "Run Examples" group.
/// </summary>
public record ShortcutDefinition
{
    private const char PathSeparator = '/';

    /// <summary>
    /// Full hierarchical name of the shortcut (required).
    /// Use "/" to create nested items, e.g., "Tools/Format Code".
    /// The display text is the last segment of the path.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Icon name from symbol registry.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Hover text; defaults to DisplayName if not specified.
    /// </summary>
    public string? Tooltip { get; init; }

    /// <summary>
    /// Python script to execute. Required for leaf items, omit for groups.
    /// </summary>
    public string? Script { get; init; }

    /// <summary>
    /// Returns the display text (last segment of the name path).
    /// </summary>
    public string DisplayName
    {
        get
        {
            var lastSeparator = Name.LastIndexOf(PathSeparator);
            return lastSeparator >= 0 ? Name[(lastSeparator + 1)..] : Name;
        }
    }

    /// <summary>
    /// Returns the parent path (everything before the last segment), or null if top-level.
    /// </summary>
    public string? ParentPath
    {
        get
        {
            var lastSeparator = Name.LastIndexOf(PathSeparator);
            return lastSeparator >= 0 ? Name[..lastSeparator] : null;
        }
    }

    /// <summary>
    /// Returns true if this shortcut is a group container (no script defined).
    /// </summary>
    public bool IsGroup => string.IsNullOrEmpty(Script);
}

/// <summary>
/// Models the shortcut configuration from the project config.
/// Contains definitions parsed from the [[shortcut]] array.
/// </summary>
public sealed record class ShortcutsSection
{
    /// <summary>
    /// List of shortcut definitions parsed from the [[shortcut]] array.
    /// </summary>
    public IReadOnlyList<ShortcutDefinition> Definitions { get; init; } = new List<ShortcutDefinition>();

    /// <summary>
    /// List of validation errors encountered during parsing.
    /// </summary>
    public IReadOnlyList<ShortcutValidationError> ValidationErrors { get; init; } = new List<ShortcutValidationError>();

    /// <summary>
    /// Returns true if there are validation errors.
    /// </summary>
    public bool HasErrors => ValidationErrors.Count > 0;
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
    /// The interim [project] table carrying the Python environment keys.
    /// </summary>
    public ProjectSection Project { get; init; } = new();

    /// <summary>
    /// Shortcut definitions from the interim top-level [[shortcut]] array.
    /// </summary>
    public ShortcutsSection Shortcuts { get; init; } = new();

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
    /// Entries that were skipped or degraded during parsing, surfaced as console banners when
    /// the workspace loads.
    /// </summary>
    public IReadOnlyList<ProjectConfigEntryError> EntryErrors { get; init; } = Array.Empty<ProjectConfigEntryError>();
}
