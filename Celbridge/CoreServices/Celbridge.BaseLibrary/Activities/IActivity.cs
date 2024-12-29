namespace Celbridge.Activities;

/// <summary>
/// Represents a long running activity that runs in the workspace.
/// </summary>
public interface IActivity
{
    /// <summary>
    /// Starts the activity.
    /// </summary>
    /// <returns></returns>
    Task<Result> Start();

    /// <summary>
    /// Stops the activity.
    /// </summary>
    Task<Result> Stop();

    /// <summary>
    /// Updates the activity.
    /// </summary>
    Task<Result> UpdateAsync();
}
