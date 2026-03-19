using Celbridge.Entities;

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
    /// Updates the annotation information for the specified entity.
    /// The entity must be of a resource type that is supported by this activity.
    /// </summary>
    Result AnnotateEntity(ResourceKey entity, IEntityAnnotation entityAnnotation);

    /// <summary>
    /// Updates the specified resource, typically by generating document data.
    /// The resource must be of a type that is supported by this activity.
    /// </summary>
    Task<Result> UpdateResourceContentAsync(ResourceKey resource, IEntityAnnotation entityAnnotation);    
}
