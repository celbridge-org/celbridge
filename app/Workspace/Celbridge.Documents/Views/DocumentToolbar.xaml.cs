using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Views;

public sealed partial class DocumentToolbar : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    private int _currentSectionCount = 1;
    private bool _isUpdatingSelection = false;

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
        _isUpdatingSelection = true;
        try
        {
            // Update radio button selection
            OneSection.IsChecked = _currentSectionCount == 1;
            TwoSections.IsChecked = _currentSectionCount == 2;
            ThreeSections.IsChecked = _currentSectionCount == 3;

            // Update the button icon to reflect current section count
            ButtonIcon.SectionCount = _currentSectionCount;
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void OneSection_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isUpdatingSelection)
        {
            SectionCountChangeRequested?.Invoke(1);
            SplitEditorFlyout.Hide();
        }
    }

    private void TwoSections_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isUpdatingSelection)
        {
            SectionCountChangeRequested?.Invoke(2);
            SplitEditorFlyout.Hide();
        }
    }

    private void ThreeSections_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isUpdatingSelection)
        {
            SectionCountChangeRequested?.Invoke(3);
            SplitEditorFlyout.Hide();
        }
    }
}
