using Celbridge.UserInterface.Helpers;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Views;

/// <summary>
/// The toolbar hosted in a collapsible document area's tab strip: a button that collapses the area. Which end
/// of the strip it sits in is the area's to decide. Splitting is driven from the document tab context menu, so
/// Main carries no toolbar.
/// </summary>
public sealed partial class DocumentToolbar : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    private readonly DocumentArea _area;

    /// <summary>
    /// Event raised when the user asks to collapse this area.
    /// </summary>
    public event Action<DocumentArea>? CollapseAreaRequested;

    public DocumentToolbar(DocumentArea area)
    {
        InitializeComponent();

        _area = area;
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        // Spotlight landmarks address this button by id, so the id stays as it is.
        var areaId = area.ToString().ToLowerInvariant();
        AutomationProperties.SetAutomationId(CollapseAreaButton, $"{areaId}-area-close-button");

        ToolTipService.SetToolTip(CollapseAreaButton, _stringLocalizer.GetString("DocumentToolbar_CollapseAreaTooltip"));

        CollapseAreaIcon.Symbol = area.GetWorkspaceArea().GetCollapseSymbol();

        // The trailing slot is a stretched column, so the toolbar has to hold itself against the end of the
        // strip it belongs to rather than floating in the middle of that column.
        HorizontalAlignment = area.PlacesToolbarAtStripStart() ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    private void CollapseAreaButton_Click(object sender, RoutedEventArgs e)
    {
        CollapseAreaRequested?.Invoke(_area);
    }
}
