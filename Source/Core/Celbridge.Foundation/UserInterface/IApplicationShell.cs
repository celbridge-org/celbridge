namespace Celbridge.UserInterface;

/// <summary>
/// The top-level application view. It shows Home while no project is loaded and the workspace while one is,
/// so which of the two is showing is a function of project state rather than a user choice.
/// </summary>
public interface IApplicationShell
{
    /// <summary>
    /// Creates the workspace view and shows it in place of Home. The cancellation source is signalled by the
    /// workspace when its load fails.
    /// </summary>
    Task<Result> ShowWorkspaceAsync(CancellationTokenSource loadCancellation);

    /// <summary>
    /// Tears the workspace view down and shows Home again. Completes once the teardown has finished.
    /// Succeeds when no workspace is showing.
    /// </summary>
    Task<Result> CloseWorkspaceAsync();
}
