/// <summary>
/// Service for managing project configuration.
/// </summary>

namespace Celbridge.Projects;

public interface IProjectConfigService
{
    /// <summary>
    /// Load project config from a file.
    /// </summary>
    Result InitializeFromFile(string filePath);

    /// <summary>Typed snapshot built from the current TOML root.</summary>
    ProjectConfig Config { get; }

    /// <summary>
    /// Check if a property exists, using a JSON-Pointer syntax.
    /// </summary>
    bool Contains(string pointer);

    /// <summary>
    /// Try to read a value, using a JSON-Pointer syntax.
    /// </summary>
    bool TryGet<T>(string pointer, out T? value);
}
