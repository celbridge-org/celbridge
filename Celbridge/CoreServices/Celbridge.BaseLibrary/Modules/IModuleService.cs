using Celbridge.Foundation;

namespace Celbridge.Modules;

/// <summary>
/// Provides services for managing modules.
/// </summary>
public interface IModuleService
{
    /// <summary>
    /// Initializes all loaded modules
    /// </summary>
    Result InitializeModules();
}
