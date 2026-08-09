namespace Celbridge.Modules;

/// <summary>
/// Provides services for managing modules.
/// </summary>
public interface IModuleService
{
    /// <summary>
    /// Returns all loaded modules.
    /// </summary>
    IReadOnlyList<IModule> LoadedModules { get; }

    /// <summary>
    /// Initializes all loaded modules.
    /// </summary>
    Result InitializeModules();
}
