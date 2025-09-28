namespace Celbridge.Projects;

/// <summary>
/// Root Celbridge project config.
/// </summary>
public sealed record class ProjectConfig
{
    public ProjectSection Project { get; init; } = new();
    public PythonSection Python { get; init; } = new();
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
