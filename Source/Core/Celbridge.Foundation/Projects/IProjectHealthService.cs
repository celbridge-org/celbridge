namespace Celbridge.Projects;

/// <summary>
/// Raised when the current project's health changes: after a load records it, and when a project
/// unloads and there is no longer any health to report.
/// </summary>
public record ProjectHealthChangedMessage(ProjectLoadReportSummary? Health);

/// <summary>
/// Holds the health of the current project as of its last load: how serious the load report was, how
/// many issues it recorded, and where to read it. Nothing can invalidate it while the user works,
/// because a change that would affect it forces a project reload, which records it again.
/// </summary>
public interface IProjectHealthService
{
    /// <summary>
    /// The current project's health, or null when no project is loaded or its load recorded none.
    /// </summary>
    ProjectLoadReportSummary? CurrentHealth { get; }

    /// <summary>
    /// Records the health of the load that just completed.
    /// </summary>
    void SetHealth(ProjectLoadReportSummary health);

    /// <summary>
    /// Drops the recorded health, leaving nothing to report until the next load.
    /// </summary>
    void ClearHealth();
}
