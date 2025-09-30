namespace Celbridge.DataTransfer;

/// <summary>
/// Specifies whether transfered data should be copied or moved.
/// </summary>
public enum DataTransferMode
{
    /// <summary>
    /// Copy the data to the destination.
    /// </summary>
    Copy,
    /// <summary>
    /// Move the data to the destination, deleting the original.
    /// This is equivalent to a cut clipboard operation.
    /// </summary>
    Move
}