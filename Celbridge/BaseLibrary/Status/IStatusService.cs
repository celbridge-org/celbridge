namespace Celbridge.Status;

/// <summary>
/// The status service provides functionality to support the status panel in the workspace UI.
/// </summary>
public interface IStatusService
{
    /// <summary>
    /// Returns the status panel view.
    /// </summary>
    IStatusPanel StatusPanel { get; }
}
