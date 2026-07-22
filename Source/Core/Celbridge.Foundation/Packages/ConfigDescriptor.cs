namespace Celbridge.Packages;

/// <summary>
/// Closed value-type vocabulary for editor config descriptors.
/// </summary>
public enum ConfigValueType
{
    /// <summary>
    /// A boolean value.
    /// </summary>
    Bool,

    /// <summary>
    /// A free-form string value.
    /// </summary>
    String,

    /// <summary>
    /// A numeric value (integer or floating point).
    /// </summary>
    Number,

    /// <summary>
    /// A string value restricted to the descriptor's declared set of allowed values.
    /// </summary>
    Enum,

    /// <summary>
    /// A list of string values.
    /// </summary>
    StringList
}

/// <summary>
/// A typed configuration key declared by an editor contribution in its manifest [[config]] entries.
/// Descriptors drive the Project Settings form, host-side type checking of contribution config, and
/// validation in agent-facing configuration tools.
/// </summary>
public partial record ConfigDescriptor
{
    /// <summary>
    /// The kebab-case key the user sets in a contribution table.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// The value type this key accepts.
    /// </summary>
    public ConfigValueType Type { get; init; }

    /// <summary>
    /// The allowed values for an Enum descriptor. Empty for every other type.
    /// </summary>
    public IReadOnlyList<string> Values { get; init; } = [];

    /// <summary>
    /// The default value in the normalized string encoding used by the editor Options channel,
    /// or null when the descriptor declares no default.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Localization key for the label shown in configuration UI.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Localization key for the longer description shown in configuration UI. May be empty.
    /// </summary>
    public string Description { get; init; } = string.Empty;
}
