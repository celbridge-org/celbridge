using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Per-resource view of a .cel sidecar's state. Distinguishes the parse-clean
/// case from each attention-state category so callers can pick behavior by
/// status rather than parsing a free-form message. Returned by IInspectCommand
/// and surfaced through the data_inspect tool.
/// </summary>
public enum SidecarStatus
{
    /// <summary>
    /// The sidecar exists and parses cleanly.
    /// </summary>
    Healthy,

    /// <summary>
    /// A sidecar file exists but its content fails to parse as TOML.
    /// </summary>
    Broken,

    /// <summary>
    /// A sidecar file exists but its parent file is missing on disk.
    /// </summary>
    Orphan,

    /// <summary>
    /// The resource key targets an invalid sidecar shape (e.g. .cel.cel),
    /// or otherwise cannot be classified as a usable sidecar.
    /// </summary>
    InvalidSidecar,

    /// <summary>
    /// The resource has no sidecar file on disk.
    /// </summary>
    NoSidecar,
}

/// <summary>
/// Per-field inventory entry inside an InspectRecord. Size is the on-disk byte
/// length of the value when serialized through the canonical TOML encoder; it
/// gives callers a cheap signal for "how big is this field" without paying the
/// full content fetch.
/// </summary>
public sealed record InspectFieldEntry(string Name, int Size);

/// <summary>
/// Per-resource entry inside an InspectResult. Status is always populated;
/// Tags and Fields are populated only on Healthy records and when summaryOnly
/// is false. ParseError is populated only when Status is Broken.
/// </summary>
public sealed record InspectRecord(
    ResourceKey Resource,
    SidecarStatus Status,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<InspectFieldEntry>? Fields,
    string? ParseError);

/// <summary>
/// Aggregate counts across a complete InspectResult, one count per status
/// category. The counts always sum to the result's record count.
/// </summary>
public sealed record InspectSummary(
    int Healthy,
    int Broken,
    int Orphan,
    int InvalidSidecar,
    int NoSidecar);

/// <summary>
/// Result of an IInspectCommand call: one record per in-scope resource, plus
/// the aggregate summary counts.
/// </summary>
public sealed record InspectResult(
    IReadOnlyList<InspectRecord> Records,
    InspectSummary Summary);

/// <summary>
/// Inspects one or more resources for sidecar health. Scope is resolved from
/// Resources and Pattern: both empty means whole project, Resources only checks
/// those specific keys, Pattern only matches via glob, and both together is the
/// union. SummaryOnly trims per-record payloads while keeping status counts.
/// </summary>
public interface IInspectCommand : IExecutableCommand<InspectResult>
{
    /// <summary>
    /// Specific resource keys to inspect. Empty when Pattern is the sole scope
    /// or when the scope is whole-project.
    /// </summary>
    IReadOnlyList<ResourceKey> Resources { get; set; }

    /// <summary>
    /// Glob pattern matched against resource keys (e.g. "assets/**"). Empty
    /// when Resources is the sole scope or when the scope is whole-project.
    /// </summary>
    string Pattern { get; set; }

    /// <summary>
    /// When true, Tags and Fields are omitted from each record; Status and
    /// ParseError remain. The aggregate summary counts are always populated.
    /// </summary>
    bool SummaryOnly { get; set; }
}
