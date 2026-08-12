namespace Celbridge.DesignTokens;

/// <summary>
/// One design token: the name it takes on each target, its value per theme, and the prose emitted
/// alongside it. A token carries either a theme invariant value or a light and dark pair.
/// </summary>
public sealed record DesignToken
{
    /// <summary>
    /// The key the token is declared under in the source file, used in diagnostics.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The resource key of the Color entry in the generated theme dictionaries, or null when the token
    /// has no XAML counterpart.
    /// </summary>
    public string? XamlColorKey { get; init; }

    /// <summary>
    /// The resource key of a SolidColorBrush generated over the colour, or null when the token needs no
    /// brush of its own.
    /// </summary>
    public string? XamlBrushKey { get; init; }

    /// <summary>
    /// The declaration name in the generated stylesheet, or null when the token has no web counterpart.
    /// </summary>
    public string? CssPropertyName { get; init; }

    /// <summary>
    /// The value used in both themes, or null when the token declares a light and dark pair instead.
    /// </summary>
    public string? ThemeInvariantValue { get; init; }

    /// <summary>
    /// The light theme value, or null when the token is theme invariant.
    /// </summary>
    public string? LightValue { get; init; }

    /// <summary>
    /// The dark theme value, or null when the token is theme invariant.
    /// </summary>
    public string? DarkValue { get; init; }

    /// <summary>
    /// Whether the CSS name is part of the contribution contract that packages outside this repository
    /// are written against.
    /// </summary>
    public bool Published { get; init; }

    /// <summary>
    /// Prose emitted above the token in the light theme block, one entry per paragraph.
    /// </summary>
    public IReadOnlyList<string> Comment { get; init; } = [];

    /// <summary>
    /// Prose emitted above the token in the dark theme block, one entry per paragraph.
    /// </summary>
    public IReadOnlyList<string> DarkComment { get; init; } = [];

    public bool EmitsXaml => XamlColorKey is not null;

    public bool EmitsCss => CssPropertyName is not null;

    public bool IsThemeInvariant => ThemeInvariantValue is not null;

    /// <summary>
    /// The value the token takes in the named theme.
    /// </summary>
    public string ValueForTheme(DesignTokenTheme theme)
    {
        if (ThemeInvariantValue is not null)
        {
            return ThemeInvariantValue;
        }

        if (theme == DesignTokenTheme.Light)
        {
            return LightValue!;
        }

        return DarkValue!;
    }
}

/// <summary>
/// A run of related tokens sharing a block of prose, emitted as one visually separated group.
/// </summary>
public sealed record DesignTokenGroup
{
    /// <summary>
    /// Prose emitted above the group in every target it reaches, one entry per paragraph.
    /// </summary>
    public IReadOnlyList<string> Comment { get; init; } = [];

    public IReadOnlyList<DesignToken> Tokens { get; init; } = [];
}

/// <summary>
/// The theme a value or comment belongs to.
/// </summary>
public enum DesignTokenTheme
{
    Light,
    Dark
}

/// <summary>
/// The whole token source: the per-target file headers and the ordered groups of tokens.
/// </summary>
public sealed record DesignTokenSource
{
    /// <summary>
    /// Prose emitted at the top of the generated resource dictionary, one entry per paragraph.
    /// </summary>
    public IReadOnlyList<string> XamlHeader { get; init; } = [];

    /// <summary>
    /// Prose emitted at the top of the generated stylesheet, one entry per paragraph.
    /// </summary>
    public IReadOnlyList<string> CssHeader { get; init; } = [];

    /// <summary>
    /// Stylesheet URLs the generated stylesheet imports, emitted above the token blocks.
    /// </summary>
    public IReadOnlyList<string> CssImports { get; init; } = [];

    public IReadOnlyList<DesignTokenGroup> Groups { get; init; } = [];

    public IEnumerable<DesignToken> Tokens => Groups.SelectMany(group => group.Tokens);
}
