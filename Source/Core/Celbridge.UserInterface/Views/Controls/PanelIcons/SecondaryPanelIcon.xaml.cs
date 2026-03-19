namespace Celbridge.UserInterface.Views.PanelIcons;

public sealed partial class SecondaryPanelIcon : UserControl
{
    public static readonly DependencyProperty IsActivePanelProperty =
        DependencyProperty.Register(
            nameof(IsActivePanel),
            typeof(bool),
            typeof(SecondaryPanelIcon),
            new PropertyMetadata(false, OnIsActivePanelChanged));

    public bool IsActivePanel
    {
        get => (bool)GetValue(IsActivePanelProperty);
        set => SetValue(IsActivePanelProperty, value);
    }

    public SecondaryPanelIcon()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Apply the current IsActivePanel state when the control loads
        UpdateVisibility(IsActivePanel);
        Loaded -= OnLoaded;
    }

    private static void OnIsActivePanelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var icon = (SecondaryPanelIcon)d;
        var isActive = (bool)e.NewValue;
        icon.UpdateVisibility(isActive);
    }

    private void UpdateVisibility(bool isActive)
    {
        // Check if elements exist before updating (they may not exist if control hasn't loaded yet)
        if (PanelFill != null)
        {
            PanelFill.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        }
        
        if (PanelDivider != null)
        {
            PanelDivider.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
