using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A FontIcon that renders a glyph from any of the bundled icon fonts, selected by IconSymbol (Symbol),
/// by prefixed icon name (IconName), or by raw glyph code (the inherited Glyph property). The font is
/// applied from whichever set the name resolves into, so call sites never reference a font or a codepoint.
/// </summary>
public sealed class Icon : FontIcon
{
    private readonly IIconService _iconService;

    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol),
        typeof(IconSymbol?),
        typeof(Icon),
        new PropertyMetadata(null, OnSymbolChanged));

    public static readonly DependencyProperty IconNameProperty = DependencyProperty.Register(
        nameof(IconName),
        typeof(string),
        typeof(Icon),
        new PropertyMetadata(string.Empty, OnIconNameChanged));

    public Icon()
    {
        _iconService = ServiceLocator.AcquireService<IIconService>();
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
    /// The prefixed icon name to display, for icons not covered by IconSymbol.
    /// </summary>
    public string IconName
    {
        get => (string)GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    private static void OnSymbolChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Icon icon && e.NewValue is IconSymbol iconSymbol)
        {
            icon.ApplyGlyph(icon._iconService.GetGlyph(iconSymbol));
        }
    }

    private static void OnIconNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Icon icon && e.NewValue is string iconName && !string.IsNullOrEmpty(iconName))
        {
            icon.ApplyGlyph(icon._iconService.GetGlyph(iconName));
        }
    }

    private void ApplyGlyph(IconGlyph glyph)
    {
        Glyph = glyph.FontCharacter;

        var fontFamily = FontFamilyConverter.Resolve(glyph.FontFamily);
        if (fontFamily is not null)
        {
            FontFamily = fontFamily;
        }
    }
}
