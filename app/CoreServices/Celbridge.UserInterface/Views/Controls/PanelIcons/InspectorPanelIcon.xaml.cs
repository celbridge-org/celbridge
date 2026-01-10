namespace Celbridge.UserInterface.Views.PanelIcons;

public sealed partial class InspectorPanelIcon : UserControl
{
    public static readonly DependencyProperty IsActivePanelProperty =
        DependencyProperty.Register(
            nameof(IsActivePanel),
            typeof(bool),
            typeof(InspectorPanelIcon),
            new PropertyMetadata(false, OnIsActivePanelChanged));

    public bool IsActivePanel
    {
        get => (bool)GetValue(IsActivePanelProperty);
        set => SetValue(IsActivePanelProperty, value);
    }

    public InspectorPanelIcon()
    {
        InitializeComponent();
    }

    private static void OnIsActivePanelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var icon = (InspectorPanelIcon)d;
        var isActive = (bool)e.NewValue;
        
        icon.PanelFill.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        icon.PanelDivider.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;
    }
}
