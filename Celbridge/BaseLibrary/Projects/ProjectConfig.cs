namespace Celbridge.Projects;

/// <summary>
/// Root Celbridge project config.
/// </summary>
public sealed record class ProjectConfig
{
    public ProjectSection Project { get; init; } = new();
    public PythonSection Python { get; init; } = new();
    public NavigationBarSection NavigationBar { get; init; } = new();
}

/// <summary>
/// Models the [project] section from the project config.
/// </summary>
public sealed record class ProjectSection
{
    public string? ProjectVersion { get; init; }
    public string? CelbridgeVersion { get; init; }

    /// <summary>
    /// [project.properties] — key/value properties from the project config.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Models the [python] section from the project config.
/// </summary>
public sealed record class PythonSection
{
    /// <summary>
    /// Python version used by the project.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// List of Python packages to install in the environment.
    /// </summary>
    public IReadOnlyList<string>? Packages { get; init; }

    /// <summary>
    /// Dictionary of Python scripts to execute at specific points in the application lifecycle.
    /// </summary>
    public IReadOnlyDictionary<string, string> Scripts { get; init; } = new Dictionary<string, string>();
}


/// <summary>
/// Models the [navigation_bar] section from the project config
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
    }

    /// <summary>
    /// Root node for our custom command graph.
    /// </summary>
    public CustomCommandNode RootCustomCommandNode { get; init; } = new CustomCommandNode();
}
