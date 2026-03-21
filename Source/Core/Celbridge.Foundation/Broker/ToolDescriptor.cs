using System.Reflection;

namespace Celbridge.Broker;

/// <summary>
/// Describes a broker tool discovered via assembly scanning.
/// Contains all metadata needed for tool listing, help generation,
/// and parameter validation.
/// </summary>
public record class ToolDescriptor
{
    /// <summary>
    /// The slash-separated tool name (e.g. "document/open").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// A short client-friendly name used for scripting APIs. Supports dot
    /// notation for namespacing (e.g. "open", "sheet.delete").
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>
    /// A human-readable description of what the tool does.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// The simplified return type name for client display (e.g. "str", "int").
    /// Empty for void methods or commands with no meaningful return value.
    /// </summary>
    public required string ReturnType { get; init; }

    /// <summary>
    /// The parameter descriptors for this tool's input schema.
    /// </summary>
    public required IReadOnlyList<ToolParameterDescriptor> Parameters { get; init; }

    /// <summary>
    /// The reflected method that implements this tool.
    /// </summary>
    public required MethodInfo Method { get; init; }
}
