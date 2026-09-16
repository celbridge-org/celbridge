using Celbridge.Documents;
using Celbridge.Reports;

namespace Celbridge.Notifications;

/// <summary>
/// Whether a notification reports a standing property of the loaded project or something that happened once.
/// </summary>
public enum NotificationKind
{
    /// <summary>
    /// A standing property of the loaded project. It holds until the source that recorded it records again or
    /// the project unloads, and the user cannot dismiss it.
    /// </summary>
    Condition,

    /// <summary>
    /// Something that happened once. It stays until the user dismisses it or the project unloads.
    /// </summary>
    Event
}

/// <summary>
/// What a notification tells the user: how serious it is and the single line it reads.
/// </summary>
public record NotificationContent(
    ReportSeverity Severity,
    string Message)
{
    /// <summary>
    /// The document the notification's action opens, or null when it offers none.
    /// </summary>
    public OpenDocumentAction? Action { get; init; }
}

/// <summary>
/// A notification waiting in the notification centre. The id stays the same for as long as the entry is
/// pending. OccurrenceCount is how many identical notifications arrived in a row, and ArrivedAt is when the
/// latest of them did.
/// </summary>
public partial record NotificationEntry(
    long Id,
    NotificationKind Kind,
    NotificationContent Content,
    DateTimeOffset ArrivedAt,
    int OccurrenceCount);
