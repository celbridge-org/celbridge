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
/// Models the [project] section from the project config.
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
/// Models the [shortcuts] section from the project config.
/// Contains shortcut definitions for various UI surfaces.
/// </summary>
public sealed record class ShortcutsSection
{
    /// <summary>
    /// Navigation bar shortcuts.
    /// </summary>
    public NavigationBarSection NavigationBar { get; init; } = new();
}

/// <summary>
/// Models the [shortcuts.navigation_bar] section from the project config
/// </summary>
public sealed record class NavigationBarSection
{
    /// <summary>
    /// Definition of a custom command in the navigation bar.
    /// </summary>
    public record class CustomCommandDefinition
    {
        public string? Icon { get; init; }
        public string? ToolTip { get; init; }
        public string? Script { get; init; }
        public string? Name { get; init; }
        public string? Path { get; init; }
    }

    /// <summary>
    /// Node in our graph of custom commands. Each node holds a list of further sub-nodes, and a list of commands for this level.
    /// </summary>
    public class CustomCommandNode
    {
        public List<CustomCommandDefinition> CustomCommands = new List<CustomCommandDefinition>();
        public Dictionary<string, CustomCommandNode> Nodes = new Dictionary<string, CustomCommandNode>();
        public string Path = "";
    }

    /// <summary>
    /// Root node for our custom command graph.
    /// </summary>
    public CustomCommandNode RootCustomCommandNode { get; init; } = new CustomCommandNode();
}
