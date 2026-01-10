namespace Celbridge.UserInterface.Views.PanelIcons;

public sealed partial class ExplorerPanelIcon : UserControl
{
    public static readonly DependencyProperty IsActivePanelProperty =
        DependencyProperty.Register(
            nameof(IsActivePanel),
            typeof(bool),
            typeof(ExplorerPanelIcon),
            new PropertyMetadata(false, OnIsActivePanelChanged));

    public bool IsActivePanel
    {
        get => (bool)GetValue(IsActivePanelProperty);
        set => SetValue(IsActivePanelProperty, value);
    }

    public ExplorerPanelIcon()
    {
        InitializeComponent();
    }

    private static void OnIsActivePanelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var icon = (ExplorerPanelIcon)d;
        var isActive = (bool)e.NewValue;
        
        icon.PanelFill.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        icon.PanelDivider.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;
    }
}
