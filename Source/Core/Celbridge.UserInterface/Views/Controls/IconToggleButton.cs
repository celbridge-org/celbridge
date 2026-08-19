using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// The toggle counterpart of IconButton, which fills with the accent while checked. Use it for an icon
/// control that stays selected, such as a rail surface, a search option, or a settings section.
/// </summary>
public sealed class IconToggleButton : ToggleButton
{
    public static readonly DependencyProperty CheckedForegroundProperty =
        DependencyProperty.Register(
            nameof(CheckedForeground),
            typeof(Brush),
            typeof(IconToggleButton),
            new PropertyMetadata(null, OnTonePropertyChanged));

    /// <summary>
    /// The tone the content is drawn in while the button is checked, chosen to read against the accent fill.
    /// </summary>
    public Brush? CheckedForeground
    {
        get => (Brush?)GetValue(CheckedForegroundProperty);
        set => SetValue(CheckedForegroundProperty, value);
    }

    public IconToggleButton()
    {
        Checked += OnCheckedChanged;
        Unchecked += OnCheckedChanged;
        Loaded += OnLoaded;

        RegisterPropertyChangedCallback(ContentProperty, OnTonePropertyChanged);
        RegisterPropertyChangedCallback(ForegroundProperty, OnTonePropertyChanged);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyContentTone();
    }

    private void OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        ApplyContentTone();
    }

    private static void OnTonePropertyChanged(DependencyObject d, DependencyProperty e)
    {
        var button = (IconToggleButton)d;
        button.ApplyContentTone();
    }

    private static void OnTonePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var button = (IconToggleButton)d;
        button.ApplyContentTone();
    }

    // The tone is pushed onto the content rather than left to the presenter's Foreground, which reaches
    // content unreliably: a FontIcon never picks it up, and content that reads Foreground through a binding
    // takes only the value it was born with. Setting it on the content element is a plain property change,
    // so every reader sees it every time.
    private void ApplyContentTone()
    {
        var tone = IsChecked == true ? CheckedForeground : Foreground;
        if (tone is null)
        {
            return;
        }

        ApplyTone(Content, tone);
    }

    private static void ApplyTone(object? element, Brush tone)
    {
        switch (element)
        {
            case IconElement iconElement:
                iconElement.Foreground = tone;
                break;

            case TextBlock textBlock:
                textBlock.Foreground = tone;
                break;

            case Control control:
                control.Foreground = tone;
                break;

            // A wrapper holding the icon alongside something like a status dot, which keeps its own brush
            // because it has no Foreground of its own.
            case Panel panel:
                foreach (var child in panel.Children)
                {
                    ApplyTone(child, tone);
                }
                break;

            case Border border:
                ApplyTone(border.Child, tone);
                break;
        }
    }
}
