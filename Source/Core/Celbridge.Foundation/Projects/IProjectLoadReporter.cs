using Celbridge.Reports;
using Celbridge.Resources;

namespace Celbridge.Projects;

/// <summary>
/// A project load report that was written to disk: where it can be opened from, how serious its worst
/// finding is, and how many findings it holds.
/// </summary>
public record ProjectLoadReportSummary(
    ResourceKey Resource,
    ReportSeverity Severity,
    int IssueCount);

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
    /// Records config entries that were skipped or degraded. Several parts of a load validate their
    /// own entries, so each call adds to what the report will hold rather than replacing it.
    /// </summary>
    void RecordConfigEntryErrors(IReadOnlyList<ProjectConfigEntryError> entryErrors);

    /// <summary>
    /// Records the package discovery outcome, including any load failures.
    /// </summary>
    void RecordPackageReport(PackageDiscoveryReport report);

    /// <summary>
    /// Records how many file and folder resources the loaded project holds.
    /// </summary>
    void RecordResourceCounts(int fileCount, int folderCount);

    /// <summary>
    /// Records the state of the project's .cel sidecar files.
    /// </summary>
    void RecordSidecarReport(SidecarReport report);

    /// <summary>
    /// Writes the current state to disk. Returns a summary of the report written, or null on
    /// failure. Never throws.
    /// </summary>
    Task<ProjectLoadReportSummary?> FlushAsync();
}
