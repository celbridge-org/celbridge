using Celbridge.UserInterface.Helpers;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A user control that displays a file icon resolved from a file extension, or from a directly bound IconDefinition.
/// </summary>
public sealed partial class FileIcon : UserControl
{
    private readonly IIconService _iconService;
    private bool _isIconDefinitionSetExternally;

    /// <summary>
    /// The file extension the icon is resolved from, with or without its leading dot.
    /// </summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(string),
        typeof(FileIcon),
        new PropertyMetadata(string.Empty, OnSourceChanged));

    /// <summary>
    /// The size of the icon in pixels.
    /// </summary>
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size),
        typeof(double),
        typeof(FileIcon),
        new PropertyMetadata(16.0, OnSizeChanged));

    /// <summary>
    /// The resolved icon definition based on the Source property, or set directly via binding.
    /// </summary>
    public static readonly DependencyProperty IconDefinitionProperty = DependencyProperty.Register(
        nameof(IconDefinition),
        typeof(IconDefinition),
        typeof(FileIcon),
        new PropertyMetadata(null, OnIconDefinitionChanged));

    public FileIcon()
    {
        _iconService = ServiceLocator.AcquireService<IIconService>();

        this.InitializeComponent();

        // The legibility adjustment depends on the theme, so recompute the foreground when it changes.
        ActualThemeChanged += (sender, args) => ApplyForeground();

        // Set default icon after InitializeComponent so bindings can override
        if (IconDefinition is null)
        {
            IconDefinition = _iconService.DefaultFileIcon;
        }

        ApplyForeground();
        ApplySize();
    }

    /// <summary>
    /// Gets or sets the file extension the icon is resolved from.
    /// </summary>
    public string Source
    {
        get => (string)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon size in pixels.
    /// </summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon definition. Can be set directly via binding or resolved from Source.
    /// </summary>
    public IconDefinition IconDefinition
    {
        get => (IconDefinition)GetValue(IconDefinitionProperty);
        set => SetValue(IconDefinitionProperty, value);
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FileIcon fileIcon)
        {
            fileIcon.UpdateIconDefinitionFromSource();
        }
    }

    private static void OnIconDefinitionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FileIcon fileIcon && e.NewValue is not null)
        {
            // Track that IconDefinition was set externally (not from Source)
            fileIcon._isIconDefinitionSetExternally = true;
            fileIcon.ApplyForeground();
            fileIcon.ApplySize();
        }
    }

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FileIcon fileIcon)
        {
            fileIcon.ApplySize();
        }
    }

    // Draws the glyph at the host's size, folded with the icon's per-glyph scale so a glyph its font draws
    // small can be enlarged to match its neighbours.
    private void ApplySize()
    {
        var scale = ParseScale(IconDefinition?.FontSize);
        IconElement.FontSize = Size * scale;
    }

    private static double ParseScale(string? percent)
    {
        if (string.IsNullOrEmpty(percent))
        {
            return 1.0;
        }

        var digits = percent.TrimEnd('%');
        if (double.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return value / 100.0;
        }

        return 1.0;
    }

    // Draws the glyph in the icon's colour, lifted or lowered as needed to stay legible on the active
    // theme's background.
    private void ApplyForeground()
    {
        var iconDefinition = IconDefinition;
        if (iconDefinition is null)
        {
            return;
        }

        var darkBackground = ActualTheme == ElementTheme.Dark;
        var legibleHex = IconColorLegibility.Normalize(iconDefinition.FontColor, darkBackground);

        IconElement.Foreground = new SolidColorBrush(HexToColor(legibleHex));
    }

    private static Windows.UI.Color HexToColor(string colorHex)
    {
        var digits = colorHex.TrimStart('#');

        byte alpha = 255;
        var offset = 0;
        if (digits.Length == 8)
        {
            alpha = byte.Parse(digits.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber);
            offset = 2;
        }

        var red = byte.Parse(digits.AsSpan(offset, 2), System.Globalization.NumberStyles.HexNumber);
        var green = byte.Parse(digits.AsSpan(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
        var blue = byte.Parse(digits.AsSpan(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);

        return Windows.UI.Color.FromArgb(alpha, red, green, blue);
    }

    private void UpdateIconDefinitionFromSource()
    {
        // If Source is being used, it takes precedence
        if (!string.IsNullOrEmpty(Source))
        {
            _isIconDefinitionSetExternally = false;

            var result = _iconService.GetFileIconForExtension(Source);
            IconDefinition = result.IsSuccess ? result.Value : _iconService.DefaultFileIcon;
        }
        else if (!_isIconDefinitionSetExternally)
        {
            // Only set default if IconDefinition wasn't set externally
            IconDefinition = _iconService.DefaultFileIcon;
        }
    }
}
