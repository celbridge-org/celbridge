namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A small colored dot that marks a status on the corner of a tab or rail button glyph. The kind picks the
/// color, and a null kind hides the pip.
/// </summary>
public sealed partial class StatusPip : UserControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind),
        typeof(StatusKind?),
        typeof(StatusPip),
        new PropertyMetadata(null, OnKindChanged));

    public StatusPip()
    {
        this.InitializeComponent();

        // Re-apply the visual state once loaded, so a kind set before the control entered the live visual
        // tree still draws its color.
        Loaded += (sender, e) => UpdateKindVisualState();
    }

    /// <summary>
    /// The kind of status the pip shows, or null to hide the pip.
    /// </summary>
    public StatusKind? Kind
    {
        get => (StatusKind?)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((StatusPip)d).UpdateKindVisualState();
    }

    private void UpdateKindVisualState()
    {
        var kind = Kind;
        if (kind is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        VisualStateManager.GoToState(this, kind.Value.ToString(), false);
        Visibility = Visibility.Visible;
    }
}
