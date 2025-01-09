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
    /// Updates an entity that is relevant to this activity.
    /// </summary>
    Task<Result> UpdateEntityAsync(ResourceKey resource);

    /// <summary>
    /// Initializes a newly created entity, if the resource is relevant to this activity.
    /// Returns true if the entity was initialized, false otherwise.
    /// </summary>
    bool TryInitializeEntity(ResourceKey resource);
}
