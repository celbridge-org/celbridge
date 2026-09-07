namespace Celbridge.Workspace;

/// <summary>
/// Timings for the workspace save model, in seconds.
/// </summary>
public static class SaveConstants
{
    /// <summary>
    /// How long an item waits after its most recent change before its content is written.
    /// </summary>
    public const double SaveDelay = 1.0;

    /// <summary>
    /// How often the save pass runs. Item save delays are measured in seconds, so this is the precision a
    /// save lands with.
    /// </summary>
    public const double SavePassInterval = 0.1;

    /// <summary>
    /// How long a failed save waits before it is attempted again.
    /// </summary>
    public const double InitialRetryDelay = 1.0;

    /// <summary>
    /// The longest a failed save waits before it is attempted again. The wait doubles with each failure up
    /// to this value, so an item that cannot be written settles into one attempt every fifteen seconds.
    /// </summary>
    public const double MaximumRetryDelay = 15.0;

    /// <summary>
    /// How long each item is given to write while the workspace closes. An item that has not written by
    /// then is abandoned and its unsaved content is lost, so that one editor cannot hold the workspace
    /// open. Long enough that only an editor which has stopped answering reaches it.
    /// </summary>
    public const double UnloadFlushTimeout = 5.0;
}
