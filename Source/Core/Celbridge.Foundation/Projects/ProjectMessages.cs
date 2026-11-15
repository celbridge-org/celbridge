namespace Celbridge.Projects;

/// <summary>
/// Sent at the end of a project load when the load report records an issue. The report holds everything the
/// load found, so the load sends one message rather than one for each issue.
/// </summary>
public record ProjectLoadIssuesReportedMessage(ProjectLoadReportSummary Summary);
