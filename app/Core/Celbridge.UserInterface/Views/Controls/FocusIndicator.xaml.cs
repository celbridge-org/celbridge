namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A focus indicator control that displays a colored bar to indicate panel focus state.
/// Shows accent color when the associated panel is focused, grey otherwise.
/// </summary>
public sealed partial class FocusIndicator : UserControl
{
    private readonly IMessengerService _messengerService;
    private readonly IPanelFocusService _panelFocusService;

    /// <summary>
    /// The panel this indicator is associated with.
    /// </summary>
    public WorkspacePanel Panel
    {
        get => (WorkspacePanel)GetValue(PanelProperty);
        set => SetValue(PanelProperty, value);
    }

    public static readonly DependencyProperty PanelProperty =
        DependencyProperty.Register(
            nameof(Panel),
            typeof(WorkspacePanel),
            typeof(FocusIndicator),
            new PropertyMetadata(WorkspacePanel.None, OnPanelChanged));

    private static void OnPanelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FocusIndicator indicator)
        {
            indicator.UpdateIndicator();
        }
    }

    public FocusIndicator()
    {
        this.InitializeComponent();

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _panelFocusService = ServiceLocator.AcquireService<IPanelFocusService>();

        Loaded += FocusIndicator_Loaded;
        Unloaded += FocusIndicator_Unloaded;
    }

    private void FocusIndicator_Loaded(object sender, RoutedEventArgs e)
    {
        _messengerService.Register<PanelFocusChangedMessage>(this, OnPanelFocusChanged);
        UpdateIndicator();
    }

    private void FocusIndicator_Unloaded(object sender, RoutedEventArgs e)
    {
        _messengerService.Unregister<PanelFocusChangedMessage>(this);
    }

    private void OnPanelFocusChanged(object recipient, PanelFocusChangedMessage message)
    {
        UpdateIndicator();
    }

    private void UpdateIndicator()
    {
        var isFocused = _panelFocusService.FocusedPanel == Panel;
        IndicatorBorder.Opacity = isFocused ? 1.0 : 0.0;
    }
}
