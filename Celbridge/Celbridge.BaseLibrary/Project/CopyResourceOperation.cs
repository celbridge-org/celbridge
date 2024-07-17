namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Specifies the behaviour of the CopyResourceCommand.
/// </summary>
public enum CopyResourceOperation
{
    /// <summary>
    /// Copy the resource to the destination.
    /// </summary>
    Copy,
    /// <summary>
    /// Move the resource to the destination.
    /// </summary>
    Move
}