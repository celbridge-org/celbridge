using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Per-field result inside an IGetFieldsCommand response. Found distinguishes
/// "field absent" from "field present with an empty value"; Value is null when
/// Found is false.
/// </summary>
public sealed record GetFieldResult(string Name, bool Found, object? Value);

/// <summary>
/// Reads a batch of named fields from the parent resource's .cel sidecar. The
/// returned list preserves the input name order. A single-element name list
/// containing the sentinel "*" returns every field on the sidecar.
/// </summary>
public interface IGetFieldsCommand : IExecutableCommand<IReadOnlyList<GetFieldResult>>
{
    /// <summary>
    /// Parent resource whose .cel sidecar will be read.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// The names to fetch. A single-entry list containing the sentinel "*"
    /// returns every field. Names starting with the reserved underscore
    /// prefix return Found=false at this surface — use the dedicated tag tools
    /// for the tag list.
    /// </summary>
    IReadOnlyList<string> Names { get; set; }
}
