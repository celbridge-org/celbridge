namespace Celbridge.Projects;

/// <summary>
/// Specifies whether a copy operation should copy or move the resource.
/// </summary>
public enum CopyResourceOperation
{
    /// <summary>
    /// Copy the resource to the destination.
    /// </summary>
    Copy,
    /// <summary>
    /// Move the resource to the destination, deleting the original.
    /// </summary>
    Move
}