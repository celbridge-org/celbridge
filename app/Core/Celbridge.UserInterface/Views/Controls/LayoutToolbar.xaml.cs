using Celbridge.Commands;
using Celbridge.Console;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Views;

public sealed partial class LayoutToolbar : UserControl
{
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly ILayoutManager _layoutManager;

    private bool _isUpdatingUI = false;
    private bool _isOnWorkspacePage = false;

    public LayoutToolbar()
    {
        InitializeComponent();

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _layoutManager = ServiceLocator.AcquireService<ILayoutManager>();

        Loaded += LayoutToolbar_Loaded;
        Unloaded += LayoutToolbar_Unloaded;
    }

    private void LayoutToolbar_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTooltips();
        ApplyLabels();
        UpdatePanelIcons();
        UpdateWindowModeRadios();
        UpdatePanelToggleVisibility();

        // Register for layout manager state change messages
        _messengerService.Register<WindowModeChangedMessage>(this, OnWindowModeChanged);
        _messengerService.Register<PanelVisibilityChangedMessage>(this, OnPanelVisibilityChanged);
        _messengerService.Register<ActivePageChangedMessage>(this, OnActivePageChanged);
    }

    private void LayoutToolbar_Unloaded(object sender, RoutedEventArgs e)
    {
        _messengerService.UnregisterAll(this);
        Loaded -= LayoutToolbar_Loaded;
        Unloaded -= LayoutToolbar_Unloaded;
    }

    private void UpdatePanelToggleVisibility()
    {
        // Show panel toggle buttons and reset layout controls only on the Workspace page
        var visibility = _isOnWorkspacePage ? Visibility.Visible : Visibility.Collapsed;

        PanelToggleButtons.Visibility = visibility;
        ResetLayoutSeparator.Visibility = visibility;
        ResetLayoutButton.Visibility = visibility;
    }

    private void ApplyTooltips()
    {
        var layoutTooltip = _stringLocalizer.GetString("LayoutToolbar_CustomizeLayoutTooltip");
        ToolTipService.SetToolTip(PanelLayoutButton, layoutTooltip);
        ToolTipService.SetPlacement(PanelLayoutButton, PlacementMode.Bottom);

        var primaryTooltip = _stringLocalizer.GetString("LayoutToolbar_TogglePrimaryTooltip");
        ToolTipService.SetToolTip(TogglePrimaryPanelButton, primaryTooltip);
        ToolTipService.SetPlacement(TogglePrimaryPanelButton, PlacementMode.Bottom);

        var consoleTooltip = _stringLocalizer.GetString("LayoutToolbar_ToggleConsoleTooltip");
        ToolTipService.SetToolTip(ToggleConsolePanelButton, consoleTooltip);
        ToolTipService.SetPlacement(ToggleConsolePanelButton, PlacementMode.Bottom);

        var secondaryTooltip = _stringLocalizer.GetString("LayoutToolbar_ToggleSecondaryTooltip");
        ToolTipService.SetToolTip(ToggleSecondaryPanelButton, secondaryTooltip);
        ToolTipService.SetPlacement(ToggleSecondaryPanelButton, PlacementMode.Bottom);

        var windowedModeTooltip = _stringLocalizer.GetString("LayoutToolbar_WindowedModeTooltip");
        ToolTipService.SetToolTip(WindowedModeRadio, windowedModeTooltip);
        ToolTipService.SetPlacement(WindowedModeRadio, PlacementMode.Bottom);

        var fullScreenModeTooltip = _stringLocalizer.GetString("LayoutToolbar_FullScreenModeTooltip");
        ToolTipService.SetToolTip(FullScreenModeRadio, fullScreenModeTooltip);
        ToolTipService.SetPlacement(FullScreenModeRadio, PlacementMode.Bottom);

        var zenModeTooltip = _stringLocalizer.GetString("LayoutToolbar_ZenModeTooltip");
        ToolTipService.SetToolTip(ZenModeRadio, zenModeTooltip);
        ToolTipService.SetPlacement(ZenModeRadio, PlacementMode.Bottom);

        var presenterModeTooltip = _stringLocalizer.GetString("LayoutToolbar_PresenterModeTooltip");
        ToolTipService.SetToolTip(PresenterModeRadio, presenterModeTooltip);
        ToolTipService.SetPlacement(PresenterModeRadio, PlacementMode.Bottom);
    }

    private void ApplyLabels()
    {
        ResetLayoutButtonText.Text = _stringLocalizer.GetString("LayoutToolbar_ResetLayoutButton");

        WindowModeHeader.Text = _stringLocalizer.GetString("LayoutToolbar_WindowModeLabel");
        WindowedModeLabel.Text = _stringLocalizer.GetString("LayoutToolbar_WindowedLabel");
        FullScreenModeLabel.Text = _stringLocalizer.GetString("LayoutToolbar_FullScreen");
        ZenModeRadioLabel.Text = _stringLocalizer.GetString("LayoutToolbar_ZenModeLabel");
        PresenterModeLabel.Text = _stringLocalizer.GetString("LayoutToolbar_PresenterLabel");
    }

    private void OnActivePageChanged(object recipient, ActivePageChangedMessage message)
    {
        _isOnWorkspacePage = message.ActivePage == ApplicationPage.Workspace;
        UpdatePanelToggleVisibility();
    }

    private void OnWindowModeChanged(object recipient, WindowModeChangedMessage message)
    {
        UpdatePanelIcons();
        UpdateWindowModeRadios();
    }

    private void OnPanelVisibilityChanged(object recipient, PanelVisibilityChangedMessage message)
    {
        UpdatePanelIcons();
    }

    private void UpdateWindowModeRadios()
    {
        _isUpdatingUI = true;
        try
        {
            var windowMode = _layoutManager.WindowMode;
            WindowedModeRadio.IsChecked = windowMode == WindowMode.Windowed;
            FullScreenModeRadio.IsChecked = windowMode == WindowMode.FullScreen;
            ZenModeRadio.IsChecked = windowMode == WindowMode.ZenMode;
            PresenterModeRadio.IsChecked = windowMode == WindowMode.Presenter;
        }
        finally
        {
            _isUpdatingUI = false;
        }
    }

    private void UpdatePanelIcons()
    {
        PrimaryPanelIcon.IsActivePanel = _layoutManager.IsContextPanelVisible;
        ConsolePanelIcon.IsActivePanel = _layoutManager.IsConsolePanelVisible;
        SecondaryPanelIcon.IsActivePanel = _layoutManager.IsInspectorPanelVisible;
    }

    private void TogglePrimaryPanelButton_Click(object sender, RoutedEventArgs e)
    {
        // Use command to toggle panel visibility
        var isVisible = !_layoutManager.IsContextPanelVisible;
        _commandService.Execute<ISetPanelVisibilityCommand>(command =>
        {
            command.Panels = PanelVisibilityFlags.Primary;
            command.IsVisible = isVisible;
        });
    }

    private void ToggleConsolePanelButton_Click(object sender, RoutedEventArgs e)
    {
        // Toggle panel visibility
        // Using an immediate command to ensure console is shown before focusing
        var isVisible = !_layoutManager.IsConsolePanelVisible;
        _commandService.ExecuteImmediate<ISetPanelVisibilityCommand>(command =>
        {
            command.Panels = PanelVisibilityFlags.Console;
            command.IsVisible = isVisible;
        });

        // Request focus when showing the console
        if (isVisible)
        {
            _messengerService.Send(new RequestConsoleFocusMessage(true));
        }
    }

    private void ToggleSecondaryPanelButton_Click(object sender, RoutedEventArgs e)
    {
        // Use command to toggle panel visibility
        var isVisible = !_layoutManager.IsInspectorPanelVisible;
        _commandService.Execute<ISetPanelVisibilityCommand>(command =>
        {
            command.Panels = PanelVisibilityFlags.Secondary;
            command.IsVisible = isVisible;
        });
    }

    private void Button_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void PanelLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        PanelLayoutFlyout.ShowAt(PanelLayoutButton);
    }

    private void ResetLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        _commandService.Execute<ISetLayoutCommand>(command =>
        {
            command.Transition = WindowModeTransition.ResetLayout;
        });
        PanelLayoutFlyout.Hide();
    }

    private void WindowModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUI)
        {
            return;
        }

        WindowModeTransition transition;

        if (ReferenceEquals(sender, WindowedModeRadio))
        {
            transition = WindowModeTransition.EnterWindowed;
        }
        else if (ReferenceEquals(sender, FullScreenModeRadio))
        {
            transition = WindowModeTransition.EnterFullScreen;
        }
        else if (ReferenceEquals(sender, ZenModeRadio))
        {
            transition = WindowModeTransition.EnterZenMode;
        }
        else if (ReferenceEquals(sender, PresenterModeRadio))
        {
            transition = WindowModeTransition.EnterPresenterMode;
        }
        else
        {
            return;
        }

        _commandService.Execute<ISetLayoutCommand>(command =>
        {
            command.Transition = transition;
        });
    }
}
