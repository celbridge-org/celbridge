using Celbridge.Projects;
using Celbridge.Reports;
using Path = System.IO.Path;

namespace Celbridge.Utilities;

/// <summary>
/// Where a project's reports are written, and how a written report is addressed as a resource.
/// </summary>
public static class ReportLocation
{
    /// <summary>
    /// Sub-folder of the project's logs folder that reports are written to.
    /// </summary>
    public const string ReportsFolderName = "reports";

    // The logs: root name, which the resource layer owns. Repeated here because it sits above this
    // project and cannot be referenced from it.
    private const string LogsRootName = "logs";

    /// <summary>
    /// The folder reports are written to for the project at the given path.
    /// </summary>
    public static string ResolveFolderPath(string projectFilePath)
    {
        var projectFolder = Path.GetDirectoryName(projectFilePath) ?? string.Empty;

        return Path.Combine(
            projectFolder,
            ProjectConstants.CelbridgeFolder,
            ProjectConstants.LogsFolder,
            ReportsFolderName);
    }

    /// <summary>
    /// The resource key a written report is opened by.
    /// </summary>
    public static ResourceKey ComposeResourceKey(string reportFilePath)
    {
        var reportFileName = Path.GetFileName(reportFilePath);

        return new ResourceKey($"{LogsRootName}:{ReportsFolderName}/{reportFileName}");
    }

    /// <summary>
    /// Writes a report for the given project and returns the resource key it can be opened by.
    /// </summary>
    public static async Task<Result<ResourceKey>> WriteReportAsync(
        IReportWriter reportWriter,
        ReportDocument report,
        string projectFilePath)
    {
        var reportsFolderPath = ResolveFolderPath(projectFilePath);

        var writeResult = await reportWriter.WriteReportAsync(report, reportsFolderPath);
        if (writeResult.IsFailure)
        {
            return Result<ResourceKey>.Fail($"Failed to write report: '{report.Id}'")
                .WithErrors(writeResult);
        }

        var reportFilePath = writeResult.Value;

        return ComposeResourceKey(reportFilePath);
    }
}
