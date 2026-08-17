namespace Celbridge.Reports;

/// <summary>
/// One kind of finding a report can hold, declared once so that the identity, wording, and severity
/// of a finding are separate from the occurrences of it. A producer names a descriptor and supplies
/// the arguments; the message is composed from the two.
/// </summary>
public record ReportFindingDescriptor(
    ReportCode Code,
    string MessageTemplate,
    ReportSeverity DefaultSeverity)
{
    /// <summary>
    /// Identifier of the help topic explaining this finding, or null until one is written.
    /// </summary>
    public string? HelpTopic { get; init; }
}
