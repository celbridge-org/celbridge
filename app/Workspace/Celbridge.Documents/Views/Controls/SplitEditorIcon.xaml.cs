namespace Celbridge.Documents.Views.Controls;

/// <summary>
/// An icon representing editor split layout with 1, 2, or 3 vertical sections.
/// Set the SectionCount property to control how many sections are displayed.
/// </summary>
public sealed partial class SplitEditorIcon : UserControl
{
    public static readonly DependencyProperty SectionCountProperty =
        DependencyProperty.Register(
            nameof(SectionCount),
            typeof(int),
            typeof(SplitEditorIcon),
            new PropertyMetadata(1, OnSectionCountChanged));

    /// <summary>
    /// Gets or sets the number of sections to display (1, 2, or 3).
    /// </summary>
    public int SectionCount
    {
        get => (int)GetValue(SectionCountProperty);
        set => SetValue(SectionCountProperty, value);
    }

    public SplitEditorIcon()
    {
        InitializeComponent();
        UpdateDividers();
    }

    private static void OnSectionCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SplitEditorIcon icon)
        {
            icon.UpdateDividers();
        }
    }

    private void UpdateDividers()
    {
        // Clamp section count to valid range
        var count = Math.Clamp(SectionCount, 1, 3);

        // 1 section: no dividers
        // 2 sections: center divider only
        // 3 sections: left and right dividers
        if (count == 1)
        {
            CenterDivider.Visibility = Visibility.Collapsed;
            LeftDivider.Visibility = Visibility.Collapsed;
            RightDivider.Visibility = Visibility.Collapsed;
        }
        else if (count == 2)
        {
            CenterDivider.Visibility = Visibility.Visible;
            LeftDivider.Visibility = Visibility.Collapsed;
            RightDivider.Visibility = Visibility.Collapsed;
        }
        else // count == 3
        {
            CenterDivider.Visibility = Visibility.Collapsed;
            LeftDivider.Visibility = Visibility.Visible;
            RightDivider.Visibility = Visibility.Visible;
        }
    }
}
