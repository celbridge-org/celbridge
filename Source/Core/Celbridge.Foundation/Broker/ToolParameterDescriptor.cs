namespace Celbridge.Broker;

/// <summary>
/// Describes a single parameter on a discovered broker tool.
/// </summary>
public record class ToolParameterDescriptor
{
    /// <summary>
    /// The parameter name as declared in the C# method signature.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The full name of the parameter's CLR type.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    /// The CLR type of the parameter.
    /// </summary>
    public required Type ParameterType { get; init; }

    /// <summary>
    /// A human-readable description from the [McpParam] attribute, or empty if not specified.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// True if the parameter has a default value in the method signature.
    /// </summary>
    public required bool HasDefaultValue { get; init; }

    /// <summary>
    /// The default value if HasDefaultValue is true, otherwise null.
    /// </summary>
    public object? DefaultValue { get; init; }
}
