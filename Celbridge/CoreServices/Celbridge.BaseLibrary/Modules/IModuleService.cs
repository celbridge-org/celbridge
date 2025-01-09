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
    /// Returns the names of the supported activities for all loaded modules.
    /// </summary>
    IReadOnlyList<string> SupportedActivities { get; }

    /// <summary>
    /// Creates an instance of a supported activity.
    /// </summary>
    Result<IActivity> CreateActivity(string activityName);
}
