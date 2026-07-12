namespace Celbridge.UserInterface.Views.Controls;

public sealed partial class ShortcutButton : UserControl
{
    public event EventHandler<RoutedEventArgs>? Click;

    public ShortcutButton()
    {
        this.InitializeComponent();
    }

    public void SetIcon(string glyphName)
    {
        IconElement.GlyphName = glyphName;
    }

    public void SetIconSize(double fontSize)
    {
        IconElement.FontSize = fontSize;
    }

    public void SetTooltip(string tooltip)
    {
        ToolTipService.SetToolTip(ButtonElement, tooltip);
        ToolTipService.SetPlacement(ButtonElement, PlacementMode.Bottom);
    }

    public void SetAutomationName(string name)
    {
        AutomationProperties.SetName(ButtonElement, name);
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
