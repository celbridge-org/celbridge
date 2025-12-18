using Celbridge.Logging;
using Tomlyn.Model;

namespace Celbridge.Projects.Services;

/// <summary>
/// Context information passed to migration steps, providing access to project data and dependencies.
/// </summary>
public class MigrationContext
{
    /// <summary>
    /// Full path to the project .celbridge file.
    /// </summary>
    public required string ProjectFilePath { get; init; }

    /// <summary>
    /// Directory containing the project file.
    /// </summary>
    public required string ProjectFolderPath { get; init; }

    /// <summary>
    /// Path to the project's celbridge metadata folder.
    /// </summary>
    public required string ProjectDataFolderPath { get; init; }

    /// <summary>
    /// Parsed TOML configuration from the project file.
    /// This property is refreshed after each migration step to reflect the current state.
    /// Migration steps can read from this but should use WriteProjectFileAsync to persist changes.
    /// </summary>
    public required TomlTable Configuration { get; set; }

    /// <summary>
    /// Logger for recording migration progress and errors.
    /// </summary>
    public required ILogger Logger { get; init; }

    /// <summary>
    /// Version string of the project before migration started.
    /// </summary>
    public required string OriginalVersion { get; init; }

    /// <summary>
    /// Helper method to write the entire project file with updated content.
    /// </summary>
    public required Func<string, Task<Result>> WriteProjectFileAsync { get; init; }
}
