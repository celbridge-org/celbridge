namespace Celbridge.Projects;

/// <summary>
/// Specifies whether transfered resources should be copied or moved.
/// </summary>
public enum ResourceTransferMode
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