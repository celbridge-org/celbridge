namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A FontIcon that renders a glyph from the shared icon font, selected by IconSymbol (Symbol), by glyph
/// name (GlyphName), or by raw glyph code (the inherited Glyph property). The icon font is applied
/// automatically, so call sites never reference a font or a codepoint.
/// </summary>
public sealed class Icon : FontIcon
{
    private readonly IIconService _iconService;

    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol),
        typeof(IconSymbol?),
        typeof(Icon),
        new PropertyMetadata(null, OnSymbolChanged));

    public static readonly DependencyProperty GlyphNameProperty = DependencyProperty.Register(
        nameof(GlyphName),
        typeof(string),
        typeof(Icon),
        new PropertyMetadata(string.Empty, OnGlyphNameChanged));

    public Icon()
    {
        _iconService = ServiceLocator.AcquireService<IIconService>();
        FontFamily = new FontFamily(_iconService.IconFontFamilyUri);
    }

    /// <summary>
    /// The icon to display, selected from the common icon set.
    /// </summary>
    public IconSymbol? Symbol
    {
        get => (IconSymbol?)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    /// <summary>
    /// The glyph name to display, for icons not covered by IconSymbol.
    /// </summary>
    public string GlyphName
    {
        get => (string)GetValue(GlyphNameProperty);
        set => SetValue(GlyphNameProperty, value);
    }

    private static void OnSymbolChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Icon icon && e.NewValue is IconSymbol iconSymbol)
        {
            icon.Glyph = icon._iconService.GetGlyph(iconSymbol);
        }
    }

    private static void OnGlyphNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Icon icon && e.NewValue is string glyphName && !string.IsNullOrEmpty(glyphName))
        {
            icon.Glyph = icon._iconService.GetGlyph(glyphName);
        }
    }
}
