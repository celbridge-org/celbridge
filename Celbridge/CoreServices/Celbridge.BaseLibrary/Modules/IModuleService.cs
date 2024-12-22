using Celbridge.Activities;

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

    /// <summary>
    /// Returns true if an activity is supported by any loaded module.
    /// </summary>
    bool IsActivitySupported(string activityName);

    /// <summary>
    /// Creates an instance of a supported activity.
    /// </summary>
    Result<IActivity> CreateActivity(string activityName);
}
