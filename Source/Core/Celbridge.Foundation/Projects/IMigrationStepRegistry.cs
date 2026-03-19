namespace Celbridge.Projects;

/// <summary>
/// Discovers and manages migration steps, providing ordered execution based on version numbers.
/// </summary>
public interface IMigrationStepRegistry
{
    /// <summary>
    /// Initializes the registry by discovering all migration step implementations.
    /// </summary>
    void Initialize();
}
