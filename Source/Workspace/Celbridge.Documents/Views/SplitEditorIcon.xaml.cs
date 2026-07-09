namespace Celbridge.Documents.Views.Controls;

/// <summary>
/// An icon representing an editor split layout, drawn as a box divided into 1, 2, or 3
/// editor sections with hinted lines of text. Set the SectionCount property to control
/// how many sections are displayed.
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
        UpdateSections();
    }

    private static void OnSectionCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SplitEditorIcon icon)
        {
            icon.UpdateSections();
        }
    }

    private void UpdateSections()
    {
        var count = Math.Clamp(SectionCount, 1, 3);

        OneSectionContent.Visibility = count == 1 ? Visibility.Visible : Visibility.Collapsed;
        TwoSectionContent.Visibility = count == 2 ? Visibility.Visible : Visibility.Collapsed;
        ThreeSectionContent.Visibility = count == 3 ? Visibility.Visible : Visibility.Collapsed;
    }
}
