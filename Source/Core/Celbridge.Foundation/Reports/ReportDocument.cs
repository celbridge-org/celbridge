namespace Celbridge.Reports;

/// <summary>
/// How serious a report, section, or item is. Determines the treatment the report
/// editor gives it and the state a status indicator derived from it shows.
/// </summary>
public enum ReportSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// What an action does when the reader activates it. Deliberately narrow: a report can be written
/// into the project and shared, so it must not be able to name arbitrary work to run, nor any
/// destination outside the project.
/// </summary>
public enum ReportActionKind
{
    /// <summary>
    /// Opens a resource as a document and reveals it in the Explorer.
    /// </summary>
    OpenResource
}

/// <summary>
/// A single thing the reader can do about an item, offered as a link or button beside it.
/// </summary>
public record ReportAction(
    ReportActionKind Kind,
    string Label)
{
    /// <summary>
    /// The resource an OpenResource action opens.
    /// </summary>
    public ResourceKey? Resource { get; init; }
}

/// <summary>
/// One row of a report section: a labelled finding or fact, optionally naming the resources it
/// concerns and the actions that address it.
/// </summary>
public record ReportItem(
    ReportSeverity Severity,
    string Message)
{
    /// <summary>
    /// The reading paired with Message as a label, which renders the row as a labelled value
    /// rather than prose. Used by summary sections.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// The resource this item is about.
    /// </summary>
    public ResourceKey? Resource { get; init; }

    /// <summary>
    /// A second resource the item relates the first to, such as the missing target of a
    /// broken reference.
    /// </summary>
    public ResourceKey? Target { get; init; }

    /// <summary>
    /// Explanatory text shown below the row.
    /// </summary>
    public string? Detail { get; init; }

    public IReadOnlyList<ReportAction> Actions { get; init; } = Array.Empty<ReportAction>();
}

/// <summary>
/// A titled group of items within a report, carrying the most serious severity among them.
/// </summary>
public record ReportSection(
    string Title,
    ReportSeverity Severity,
    IReadOnlyList<ReportItem> Items);

/// <summary>
/// What a producer left out, so a report never silently claims to be complete.
/// </summary>
public record ReportTruncation(int Omitted);

/// <summary>
/// The structured outcome of one completed operation: a generated, read-only record of what
/// happened, written to a file and opened as a document.
/// </summary>
public partial record ReportDocument(
    string Id,
    string Title,
    DateTimeOffset GeneratedAt,
    ReportSeverity Severity,
    string Summary,
    IReadOnlyList<ReportSection> Sections)
{
    /// <summary>
    /// The schema version of the serialized form.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// File extension for serialized reports.
    /// </summary>
    public const string FileExtension = ".report";

    /// <summary>
    /// The schema version this document was written against.
    /// </summary>
    public int Version { get; init; } = CurrentVersion;

    public ReportTruncation? Truncated { get; init; }
}
