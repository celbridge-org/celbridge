namespace Celbridge.Reports;

/// <summary>
/// Serializes report documents to a folder and enforces their retention.
/// </summary>
public interface IReportWriter
{
    /// <summary>
    /// Writes a report into the folder, returning the path it was written to. Each write is a
    /// new file, never a replacement for an earlier one.
    /// </summary>
    Task<Result<string>> WriteReportAsync(ReportDocument report, string folderPath);
}
