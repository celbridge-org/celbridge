namespace Celbridge.Reports;

/// <summary>
/// Serializes report documents to a folder and enforces their retention.
/// </summary>
public interface IReportWriter
{
    /// <summary>
    /// Writes a report as the current report for its id, returning the path it was written to. Any
    /// previous report for that id is moved into the history sub-folder rather than lost, and the
    /// oldest history entries beyond the retention limit are deleted.
    /// </summary>
    Task<Result<string>> WriteReportAsync(ReportDocument report, string folderPath);
}
