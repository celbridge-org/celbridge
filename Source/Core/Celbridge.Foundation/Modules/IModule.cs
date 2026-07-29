using Celbridge.Activities;
using Celbridge.Documents;

namespace Celbridge.Modules;

/// <summary>
/// The module system discovers classes that implement this interface at startup.
/// </summary>
public interface IModule
{
    /// <summary>
    /// Configures the dependency injection framework to support the types provided by the module.
    /// </summary>
    void ConfigureServices(IModuleServiceCollection serviceCollection);

    /// <summary>
    /// Initializes the module during application startup.
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

    /// <summary>
    /// Creates document editor factories provided by this module.
    /// </summary>
    IReadOnlyList<IDocumentEditorFactory> CreateDocumentEditorFactories(IServiceProvider serviceProvider);
}
