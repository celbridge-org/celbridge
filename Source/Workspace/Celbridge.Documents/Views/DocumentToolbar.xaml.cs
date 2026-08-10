using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Views;

/// <summary>
/// The toolbar hosted in a collapsible document area's tab strip footer: a button that closes the area.
/// Splitting is driven from the document tab context menu, so Main carries no toolbar.
/// </summary>
public sealed partial class DocumentToolbar : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    private readonly DocumentArea _area;

    /// <summary>
    /// Event raised when the user asks to collapse this area.
    /// </summary>
    public event Action<DocumentArea>? CloseAreaRequested;

    public DocumentToolbar(DocumentArea area)
    {
        InitializeComponent();

        _area = area;
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var areaId = area.ToString().ToLowerInvariant();
        AutomationProperties.SetAutomationId(CloseAreaButton, $"{areaId}-area-close-button");

        ToolTipService.SetToolTip(CloseAreaButton, _stringLocalizer.GetString("DocumentToolbar_CloseAreaTooltip"));
    }

    private void CloseAreaButton_Click(object sender, RoutedEventArgs e)
    {
        CloseAreaRequested?.Invoke(_area);
    }
}
