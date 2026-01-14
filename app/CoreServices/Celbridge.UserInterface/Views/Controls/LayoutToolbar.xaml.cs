using Celbridge.Commands;
using Celbridge.Settings;

namespace Celbridge.UserInterface.Views;

public sealed partial class LayoutToolbar : UserControl
{
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly ILayoutManager _layoutManager;
    private readonly IEditorSettings _editorSettings;

    private bool _isUpdatingUI = false;
    private bool _isOnWorkspacePage = false;

    public LayoutToolbar()
    {
        InitializeComponent();

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _layoutManager = ServiceLocator.AcquireService<ILayoutManager>();
        _editorSettings = ServiceLocator.AcquireService<IEditorSettings>();

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

        var explorerTooltip = _stringLocalizer.GetString("LayoutToolbar_ToggleExplorerTooltip");
        ToolTipService.SetToolTip(ToggleExplorerPanelButton, explorerTooltip);
        ToolTipService.SetPlacement(ToggleExplorerPanelButton, PlacementMode.Bottom);

        var consoleTooltip = _stringLocalizer.GetString("LayoutToolbar_ToggleConsoleTooltip");
        ToolTipService.SetToolTip(ToggleConsolePanelButton, consoleTooltip);
        ToolTipService.SetPlacement(ToggleConsolePanelButton, PlacementMode.Bottom);

        var inspectorTooltip = _stringLocalizer.GetString("LayoutToolbar_ToggleInspectorTooltip");
        ToolTipService.SetToolTip(ToggleInspectorPanelButton, inspectorTooltip);
        ToolTipService.SetPlacement(ToggleInspectorPanelButton, PlacementMode.Bottom);

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
        ExplorerPanelIcon.IsActivePanel = _layoutManager.IsContextPanelVisible;
        ConsolePanelIcon.IsActivePanel = _layoutManager.IsConsolePanelVisible;
        InspectorPanelIcon.IsActivePanel = _layoutManager.IsInspectorPanelVisible;
    }

    private void ToggleExplorerPanelButton_Click(object sender, RoutedEventArgs e)
    {
        // Use command to toggle panel visibility
        var isVisible = !_layoutManager.IsContextPanelVisible;
        _commandService.Execute<ISetPanelVisibilityCommand>(command =>
        {
            command.Panels = PanelVisibilityFlags.Context;
            command.IsVisible = isVisible;
        });
    }

    private void ToggleConsolePanelButton_Click(object sender, RoutedEventArgs e)
    {
        // Use command to toggle panel visibility
        var isVisible = !_layoutManager.IsConsolePanelVisible;
        _commandService.Execute<ISetPanelVisibilityCommand>(command =>
        {
            command.Panels = PanelVisibilityFlags.Console;
            command.IsVisible = isVisible;
        });
    }

    private void ToggleInspectorPanelButton_Click(object sender, RoutedEventArgs e)
    {
        // Use command to toggle panel visibility
        var isVisible = !_layoutManager.IsInspectorPanelVisible;
        _commandService.Execute<ISetPanelVisibilityCommand>(command =>
        {
            command.Panels = PanelVisibilityFlags.Inspector;
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
            command.Transition = LayoutTransition.ResetLayout;
        });
        PanelLayoutFlyout.Hide();
    }

    private void WindowModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUI)
        {
            return;
        }

        LayoutTransition transition;
        
        if (ReferenceEquals(sender, WindowedModeRadio))
        {
            transition = LayoutTransition.EnterWindowed;
        }
        else if (ReferenceEquals(sender, FullScreenModeRadio))
        {
            transition = LayoutTransition.EnterFullScreen;
        }
        else if (ReferenceEquals(sender, ZenModeRadio))
        {
            transition = LayoutTransition.EnterZenMode;
        }
        else if (ReferenceEquals(sender, PresenterModeRadio))
        {
            transition = LayoutTransition.EnterPresenterMode;
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
