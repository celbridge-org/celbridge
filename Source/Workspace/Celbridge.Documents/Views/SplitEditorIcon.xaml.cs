namespace Celbridge.Documents.Views.Controls;

/// <summary>
/// An icon representing an editor split layout, drawn as a box that is either whole or divided in two
/// with hinted lines of text. The divider follows the split orientation of the area the icon stands for.
/// </summary>
public sealed partial class SplitEditorIcon : UserControl
{
    public static readonly DependencyProperty ShowsTwoSectionsProperty =
        DependencyProperty.Register(
            nameof(ShowsTwoSections),
            typeof(bool),
            typeof(SplitEditorIcon),
            new PropertyMetadata(false, OnLayoutChanged));

    public static readonly DependencyProperty SplitsHorizontallyProperty =
        DependencyProperty.Register(
            nameof(SplitsHorizontally),
            typeof(bool),
            typeof(SplitEditorIcon),
            new PropertyMetadata(true, OnLayoutChanged));

    /// <summary>
    /// Gets or sets whether the icon shows a divided box rather than a whole one.
    /// </summary>
    public bool ShowsTwoSections
    {
        get => (bool)GetValue(ShowsTwoSectionsProperty);
        set => SetValue(ShowsTwoSectionsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether a divided box is split by a vertical divider rather than a horizontal one.
    /// </summary>
    public bool SplitsHorizontally
    {
        get => (bool)GetValue(SplitsHorizontallyProperty);
        set => SetValue(SplitsHorizontallyProperty, value);
    }

    public SplitEditorIcon()
    {
        InitializeComponent();
        UpdateSections();
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SplitEditorIcon icon)
        {
            icon.UpdateSections();
        }
    }

    private void UpdateSections()
    {
        bool showsHorizontalSplit = ShowsTwoSections && SplitsHorizontally;
        bool showsVerticalSplit = ShowsTwoSections && !SplitsHorizontally;

        OneSectionContent.Visibility = ShowsTwoSections ? Visibility.Collapsed : Visibility.Visible;
        TwoSectionsHorizontalContent.Visibility = showsHorizontalSplit ? Visibility.Visible : Visibility.Collapsed;
        TwoSectionsVerticalContent.Visibility = showsVerticalSplit ? Visibility.Visible : Visibility.Collapsed;
    }
}
