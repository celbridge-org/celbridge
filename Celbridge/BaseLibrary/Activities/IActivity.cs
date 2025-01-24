namespace Celbridge.Activities;

/// <summary>
/// Represents a long running activity that runs in the workspace.
/// </summary>
public interface IActivity
{
    /// <summary>
    /// Activates the activity when the workspace loads.
    /// </summary>
    /// <returns></returns>
    Task<Result> ActivateAsync();

    /// <summary>
    /// Deactivate the activity when the workspace unloads.
    /// </summary>
    Task<Result> DeactivateAsync();

    /// <summary>
    /// Returns true if the activity supports the resource.
    /// </summary>
    bool SupportsResource(ResourceKey resource);

    /// <summary>
    /// Initializes a newly created resource that is supported by this activity.
    /// </summary>
    Task<Result> InitializeResourceAsync(ResourceKey resource);

    /// <summary>
    /// Updates a resource that is supported by this activity..
    /// </summary>
    Task<Result> UpdateResourceAsync(ResourceKey resource);
}
