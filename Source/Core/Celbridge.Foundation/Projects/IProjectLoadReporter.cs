using Celbridge.Resources;

namespace Celbridge.Projects;

/// <summary>
/// Accumulates project-load state and writes it to a Markdown report on flush.
/// </summary>
public interface IProjectLoadReporter
{
    /// <summary>
    /// Resets state for a fresh project load.
    /// </summary>
    void BeginLoad(string projectFilePath);

    /// <summary>
    /// Records the migration outcome and the user's upgrade-dialog decision.
    /// </summary>
    void RecordMigrationResult(MigrationResult result, bool userConfirmedUpgrade, bool userCancelledUpgrade);

    /// <summary>
    /// Records the project load outcome.
    /// </summary>
    void RecordLoadOutcome(bool loadSucceeded, Result? loadResult);

    /// <summary>
    /// Records the consistency-check findings.
    /// </summary>
    void RecordCheckReport(ProjectCheckReport report);

    /// <summary>
    /// Writes the current state to disk. Returns the report path on success,
    /// or null on failure. Never throws.
    /// </summary>
    Task<string?> FlushAsync();
}
