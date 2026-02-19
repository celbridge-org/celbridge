namespace Celbridge.Projects;

/// <summary>
/// Root Celbridge project config.
/// </summary>
public sealed record class ProjectConfig
{
    public ProjectSection Project { get; init; } = new();
    public CelbridgeSection Celbridge { get; init; } = new();
    public ShortcutsSection Shortcuts { get; init; } = new();
}

/// <summary>
/// Models the [project] section from the .celbridge project config.
/// Uses pyproject.toml naming conventions for Python-related fields.
/// The [project] section can be copied to a pyproject.toml file for use with 
/// the Python packaging tools such as twine. 
/// Note that the requires-python field in pyproject.toml expects a version range, e.g. ">=3.12" rather 
/// than a specific version number.
/// </summary>
public sealed record class ProjectSection
{
    /// <summary>
    /// Project name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Project version.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Python version requirement (e.g., ">=3.12").
    /// </summary>
    public string? RequiresPython { get; init; }

    /// <summary>
    /// List of Python package dependencies to install in the environment.
    /// </summary>
    public IReadOnlyList<string>? Dependencies { get; init; }

    /// <summary>
    /// [project.properties] — key/value properties from the project config.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Models the [celbridge] section from the project config.
/// Contains Celbridge-specific settings.
/// </summary>
public sealed record class CelbridgeSection
{
    /// <summary>
    /// Version of Celbridge used to create/last modify the project.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Dictionary of Python scripts to execute at specific points in the application lifecycle.
    /// </summary>
    public IReadOnlyDictionary<string, string> Scripts { get; init; } = new Dictionary<string, string>();
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
