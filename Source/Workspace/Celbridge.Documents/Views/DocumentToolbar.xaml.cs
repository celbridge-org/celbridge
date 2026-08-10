using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Views;

/// <summary>
/// The toolbar hosted in a document area's tab strip footer: a button that splits and unsplits the area,
/// and for the collapsible areas a button that closes them.
/// </summary>
public sealed partial class DocumentToolbar : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    private readonly DocumentArea _area;

    private bool _isAreaSplit = false;

    /// <summary>
    /// Event raised when the user asks to split or unsplit this area.
    /// </summary>
    public event Action<DocumentArea, bool>? SplitChangeRequested;

    /// <summary>
    /// Event raised when the user asks to collapse this area.
    /// </summary>
    public event Action<DocumentArea>? CloseAreaRequested;

    public DocumentToolbar(DocumentArea area)
    {
        InitializeComponent();

        _area = area;
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        ButtonIcon.SplitsHorizontally = area.SplitsHorizontally();

        var areaId = area.ToString().ToLowerInvariant();
        AutomationProperties.SetAutomationId(SplitEditorButton, $"{areaId}-area-split-button");
        AutomationProperties.SetAutomationId(CloseAreaButton, $"{areaId}-area-close-button");

        if (area.IsCollapsible())
        {
            CloseAreaButton.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(CloseAreaButton, _stringLocalizer.GetString("DocumentToolbar_CloseAreaTooltip"));
        }

        UpdateSplitState(isAreaSplit: false);
    }

    /// <summary>
    /// Updates the toolbar to reflect whether its area is currently split. The icon and tooltip describe
    /// what the button does next rather than the current state.
    /// </summary>
    public void UpdateSplitState(bool isAreaSplit)
    {
        _isAreaSplit = isAreaSplit;

        ButtonIcon.ShowsTwoSections = !isAreaSplit;

        string tooltipKey = isAreaSplit
            ? "DocumentToolbar_UnsplitEditorTooltip"
            : "DocumentToolbar_SplitEditorTooltip";
        ToolTipService.SetToolTip(SplitEditorButton, _stringLocalizer.GetString(tooltipKey));
    }

    /// <summary>
    /// Enables or disables the split button. The area is too small to hold two sections when disabled.
    /// </summary>
    public void UpdateSplitAvailable(bool canSplit)
    {
        // An area that is already split can always be unsplit, whatever its size.
        SplitEditorButton.IsEnabled = _isAreaSplit || canSplit;
    }

    private void SplitEditorButton_Click(object sender, RoutedEventArgs e)
    {
        SplitChangeRequested?.Invoke(_area, !_isAreaSplit);
    }

    private void CloseAreaButton_Click(object sender, RoutedEventArgs e)
    {
        CloseAreaRequested?.Invoke(_area);
    }
}
