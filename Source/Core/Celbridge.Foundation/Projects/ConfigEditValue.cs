namespace Celbridge.Projects;

/// <summary>
/// A typed configuration value the Project Settings editor sets on a contribution. The five variants
/// mirror the closed config descriptor type vocabulary, and the draft maps each to its TOML value form.
/// </summary>
public abstract record ConfigEditValue;

/// <summary>
/// A boolean config value, written as a TOML true or false literal.
/// </summary>
public sealed record BoolEditValue(bool Value) : ConfigEditValue;

/// <summary>
/// A string config value (also used for enum selections), written as a TOML basic string.
/// </summary>
public sealed record StringEditValue(string Value) : ConfigEditValue;

/// <summary>
/// An integer config value, written as a TOML integer literal.
/// </summary>
public sealed record IntegerEditValue(long Value) : ConfigEditValue;

/// <summary>
/// A floating-point config value, written as a TOML float literal.
/// </summary>
public sealed record FloatEditValue(double Value) : ConfigEditValue;

/// <summary>
/// A string-list config value, written as an inline TOML array of basic strings.
/// </summary>
public sealed record StringListEditValue(IReadOnlyList<string> Values) : ConfigEditValue;
