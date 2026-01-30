using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Views;

public sealed partial class DocumentToolbar : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    private int _currentSectionCount = 1;

    // Toolbar tooltip strings
    private string SplitEditorTooltipString => _stringLocalizer.GetString("DocumentToolbar_SplitEditorTooltip");

    // Flyout menu strings
    private string OneSectionString => _stringLocalizer.GetString("DocumentToolbar_OneSection");
    private string TwoSectionsString => _stringLocalizer.GetString("DocumentToolbar_TwoSections");
    private string ThreeSectionsString => _stringLocalizer.GetString("DocumentToolbar_ThreeSections");

    /// <summary>
    /// Event raised when the user requests a change in the number of sections.
    /// </summary>
    public event Action<int>? SectionCountChangeRequested;

    public DocumentToolbar()
    {
        InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        UpdateMenuItemStates();
    }

    /// <summary>
    /// Updates the toolbar to reflect the current section count.
    /// </summary>
    public void UpdateSectionCount(int sectionCount)
    {
        _currentSectionCount = sectionCount;
        UpdateMenuItemStates();
    }

    private void UpdateMenuItemStates()
    {
        // Use a checkmark or similar indicator for the current selection
        // WinUI MenuFlyoutItem doesn't have IsChecked, so we use FontWeight
        OneSection.FontWeight = _currentSectionCount == 1
            ? Microsoft.UI.Text.FontWeights.Bold
            : Microsoft.UI.Text.FontWeights.Normal;
        TwoSections.FontWeight = _currentSectionCount == 2
            ? Microsoft.UI.Text.FontWeights.Bold
            : Microsoft.UI.Text.FontWeights.Normal;
        ThreeSections.FontWeight = _currentSectionCount == 3
            ? Microsoft.UI.Text.FontWeights.Bold
            : Microsoft.UI.Text.FontWeights.Normal;
    }

    private void OneSection_Click(object sender, RoutedEventArgs e)
    {
        SectionCountChangeRequested?.Invoke(1);
    }

    private void TwoSections_Click(object sender, RoutedEventArgs e)
    {
        SectionCountChangeRequested?.Invoke(2);
    }

    private void ThreeSections_Click(object sender, RoutedEventArgs e)
    {
        SectionCountChangeRequested?.Invoke(3);
    }
}
