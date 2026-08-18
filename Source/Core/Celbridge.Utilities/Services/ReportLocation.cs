using System.Text.RegularExpressions;
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

    // Groups of lowercase letters and digits, separated by single hyphens or dots. A package
    // qualifies a generic kind with its own name, so the dotted form an editor id takes is allowed.
    private static readonly Regex ReportIdRegex = new(@"^[a-z0-9]+([-.][a-z0-9]+)*$", RegexOptions.Compiled);

    /// <summary>
    /// Returns true if the id can serve as a report id. It becomes a file name, a history file name,
    /// and the glob that prunes that history, so a separator, a "..", or a glob metacharacter in one
    /// would reach outside the reports folder or match the wrong files.
    /// </summary>
    public static bool IsValidReportId(string? reportId)
    {
        if (string.IsNullOrEmpty(reportId))
        {
            return false;
        }

        return ReportIdRegex.IsMatch(reportId);
    }

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
