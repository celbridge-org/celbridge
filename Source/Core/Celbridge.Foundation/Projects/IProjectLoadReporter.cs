namespace Celbridge.Projects;

/// <summary>
/// Accumulates project-load state and writes it as a report document on flush.
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
    /// Records the package discovery outcome, including any load failures.
    /// </summary>
    void RecordPackageReport(PackageDiscoveryReport report);

    /// <summary>
    /// Records how many file and folder resources the loaded project holds.
    /// </summary>
    void RecordResourceCounts(int fileCount, int folderCount);

    /// <summary>
    /// Records the consistency-check findings.
    /// </summary>
    void RecordCheckReport(ProjectCheckReport report);

    /// <summary>
    /// Writes the current state to disk. Returns the resource key of the report written,
    /// or null on failure. Never throws.
    /// </summary>
    Task<ResourceKey?> FlushAsync();
}
