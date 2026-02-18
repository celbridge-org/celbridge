namespace Celbridge.WorkspaceUI.Views;

public sealed partial class ShortcutButton : UserControl
{
    public event EventHandler<RoutedEventArgs>? Click;

    public ShortcutButton()
    {
        this.InitializeComponent();
    }

    public void SetIcon(Symbol symbol)
    {
        IconElement.Symbol = symbol;
    }

    public void SetTooltip(string tooltip)
    {
        ToolTipService.SetToolTip(ButtonElement, tooltip);
        ToolTipService.SetPlacement(ButtonElement, PlacementMode.Right);
    }

    public void SetFlyout(MenuFlyout flyout)
    {
        ButtonElement.Flyout = flyout;
    }

    private void ButtonElement_Click(object sender, RoutedEventArgs e)
    {
        Click?.Invoke(this, e);
    }
}
