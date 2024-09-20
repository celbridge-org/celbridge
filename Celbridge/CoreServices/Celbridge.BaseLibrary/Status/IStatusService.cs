namespace Celbridge.Status;

/// <summary>
/// The status service provides functionality to support the status panel in the workspace UI.
/// </summary>
public interface IStatusService
{
    /// <summary>
    /// Factory method to create the status panel for the workspace UI.
    /// </summary>
    IStatusPanel CreateStatusPanel();
}
