using Celbridge.Activities;

namespace Celbridge.Modules;

/// <summary>
/// The module system discovers classes that implement this interface at startup.
/// All Celbridge modules must contain a class that implements this interface.
/// </summary>
public interface IModule
{
    /// <summary>
    /// Configures the dependency injection framework to support the types provided by the extension.
    /// </summary>
    void ConfigureServices(IModuleServiceCollection serviceCollection);

    /// <summary>
    /// Initializes the extension during application startup.
    /// </summary>
    Result Initialize();

    /// <summary>
    /// Returns the names of all activities supported by this module.
    /// </summary>
    IReadOnlyList<string> SupportedActivities { get; }

    /// <summary>
    /// Creates an instance of a supported activity.
    /// </summary>
    Result<IActivity> CreateActivity(string activityName);
}
