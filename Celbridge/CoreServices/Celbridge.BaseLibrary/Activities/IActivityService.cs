namespace Celbridge.Activities;

/// <summary>
/// A service that manages long-running activities in a workspace.
/// </summary>
public interface IActivityService
{
    /// <summary>
    /// Initializes the activity service.
    /// </summary>
    Task<Result> Initialize();

    /// <summary>
    /// Updates the activities in the workspace.
    /// </summary>
    Task<Result> UpdateAsync();
}
