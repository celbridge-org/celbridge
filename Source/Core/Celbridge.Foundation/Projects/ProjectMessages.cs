namespace Celbridge.Projects;

/// <summary>
/// Raised once at the end of a project load whose report recorded something worth telling the user
/// about. Everything the load found is in the report this names, so the load raises one message rather
/// than a notification per condition.
/// </summary>
public record ProjectLoadNotificationMessage(ProjectLoadReportSummary Summary);
