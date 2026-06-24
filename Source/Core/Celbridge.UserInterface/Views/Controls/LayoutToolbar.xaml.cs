using Celbridge.Commands;
using Celbridge.Console;
using Celbridge.Settings;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Views;

public sealed partial class LayoutToolbar : UserControl
{
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWindowModeService _windowModeService;
    private readonly ILayoutService _layoutService;
    private readonly IFeatureFlags _featureFlags;

    private bool _isUpdatingUI = false;
    private bool _isOnWorkspacePage = false;

    public LayoutToolbar()
    {
        InitializeComponent();

#if !WINDOWS
        // macOS provides fullscreen natively through the title-bar green button, so the app does not
        // offer its own Full Screen toggle. The Default/Focus/Presentation layout modes remain available.
        FullScreenToggle.Visibility = Visibility.Collapsed;
#endif

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _windowModeService = ServiceLocator.AcquireService<IWindowModeService>();
        _layoutService = ServiceLocator.AcquireService<ILayoutService>();
        _featureFlags = ServiceLocator.AcquireService<IFeatureFlags>();

        Loaded += LayoutToolbar_Loaded;
        Unloaded += LayoutToolbar_Unloaded;
    }

    private void LayoutToolbar_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTooltips();
        ApplyLabels();
        UpdatePanelIcons();
        UpdateLayoutModeRadios();
        UpdateFullScreenToggle();
        UpdateWorkspaceOnlyControlsVisibility();

        // Register for layout manager state change messages
        _messengerService.Register<LayoutModeChangedMessage>(this, OnLayoutModeChanged);
        _messengerService.Register<FullScreenChangedMessage>(this, OnFullScreenChanged);
        _messengerService.Register<RegionVisibilityChangedMessage>(this, OnRegionVisibilityChanged);
        _messengerService.Register<ActivePageChangedMessage>(this, OnActivePageChanged);
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
        _messengerService.Register<FeatureFlagsChangedMessage>(this, OnFeatureFlagsChanged);
    }

    private void LayoutToolbar_Unloaded(object sender, RoutedEventArgs e)
    {
        _messengerService.UnregisterAll(this);
        Loaded -= LayoutToolbar_Loaded;
        Unloaded -= LayoutToolbar_Unloaded;
    }

    private void UpdateWorkspaceOnlyControlsVisibility()
    {
        // Panel toggles, layout-mode radios, and the reset button only make sense on the Workspace page.
        // The Full Screen toggle stays available everywhere because it is a window-level concern.
        var visibility = _isOnWorkspacePage ? Visibility.Visible : Visibility.Collapsed;

        PanelToggleButtons.Visibility = visibility;
        ResetLayoutSeparator.Visibility = visibility;
        ResetLayoutButton.Visibility = visibility;

        WindowModeHeader.Visibility = visibility;
        DefaultModeRadio.Visibility = visibility;
        FocusModeRadio.Visibility = visibility;
        PresentationModeRadio.Visibility = visibility;

        // Hide console panel toggle button if console-panel feature is disabled
        var isConsolePanelEnabled = _featureFlags.IsEnabled(FeatureFlagConstants.ConsolePanel);
        ToggleConsolePanelButton.Visibility = (visibility == Visibility.Visible && isConsolePanelEnabled)
            ? Visibility.Visible
            : Visibility.Collapsed;
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

        var defaultModeTooltip = _stringLocalizer.GetString("LayoutToolbar_DefaultModeTooltip");
        ToolTipService.SetToolTip(DefaultModeRadio, defaultModeTooltip);
        ToolTipService.SetPlacement(DefaultModeRadio, PlacementMode.Bottom);

        var focusModeTooltip = _stringLocalizer.GetString("LayoutToolbar_FocusModeTooltip");
        ToolTipService.SetToolTip(FocusModeRadio, focusModeTooltip);
        ToolTipService.SetPlacement(FocusModeRadio, PlacementMode.Bottom);

        var presentationModeTooltip = _stringLocalizer.GetString("LayoutToolbar_PresentationModeTooltip");
        ToolTipService.SetToolTip(PresentationModeRadio, presentationModeTooltip);
        ToolTipService.SetPlacement(PresentationModeRadio, PlacementMode.Bottom);

        var fullScreenTooltip = _stringLocalizer.GetString("LayoutToolbar_FullScreenModeTooltip");
        ToolTipService.SetToolTip(FullScreenToggle, fullScreenTooltip);
        ToolTipService.SetPlacement(FullScreenToggle, PlacementMode.Bottom);
    }

    private void ApplyLabels()
    {
        ResetLayoutButtonText.Text = _stringLocalizer.GetString("LayoutToolbar_ResetLayoutButton");

        WindowModeHeader.Text = _stringLocalizer.GetString("LayoutToolbar_LayoutModeLabel");
        DefaultModeLabel.Text = _stringLocalizer.GetString("LayoutToolbar_DefaultLabel");
        FocusModeLabel.Text = _stringLocalizer.GetString("LayoutToolbar_FocusLabel");
        PresentationModeLabel.Text = _stringLocalizer.GetString("LayoutToolbar_PresentationLabel");
        FullScreenLabel.Text = _stringLocalizer.GetString("LayoutToolbar_FullScreen");
    }

    private void OnActivePageChanged(object recipient, ActivePageChangedMessage message)
    {
        _isOnWorkspacePage = message.ActivePage == ApplicationPage.Workspace;
        UpdateWorkspaceOnlyControlsVisibility();
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        // Update button visibility when workspace loads (feature flags may have changed)
        UpdateWorkspaceOnlyControlsVisibility();
    }

    private void OnFeatureFlagsChanged(object recipient, FeatureFlagsChangedMessage message)
    {
        UpdateWorkspaceOnlyControlsVisibility();
    }

    private void OnLayoutModeChanged(object recipient, LayoutModeChangedMessage message)
    {
        UpdatePanelIcons();
        UpdateLayoutModeRadios();
    }

    private void OnFullScreenChanged(object recipient, FullScreenChangedMessage message)
    {
        UpdateFullScreenToggle();
    }

    private void OnRegionVisibilityChanged(object recipient, RegionVisibilityChangedMessage message)
    {
        UpdatePanelIcons();
    }

    private void UpdateLayoutModeRadios()
    {
        _isUpdatingUI = true;
        try
        {
            var layoutMode = _windowModeService.LayoutMode;
            DefaultModeRadio.IsChecked = layoutMode == LayoutMode.Default;
            FocusModeRadio.IsChecked = layoutMode == LayoutMode.Focus;
            PresentationModeRadio.IsChecked = layoutMode == LayoutMode.Presentation;
        }
        finally
        {
            _isUpdatingUI = false;
        }
    }

    private void UpdateFullScreenToggle()
    {
        _isUpdatingUI = true;
        try
        {
            FullScreenToggle.IsChecked = _windowModeService.IsFullScreen;
        }
        finally
        {
            _isUpdatingUI = false;
        }
    }

    private void UpdatePanelIcons()
    {
        PrimaryPanelIcon.IsActivePanel = _layoutService.IsContextPanelVisible;
        ConsolePanelIcon.IsActivePanel = _layoutService.IsConsolePanelVisible;
        SecondaryPanelIcon.IsActivePanel = _layoutService.IsInspectorPanelVisible;
    }

    private void TogglePrimaryPanelButton_Click(object sender, RoutedEventArgs e)
    {
        // Use command to toggle panel visibility
        var isVisible = !_layoutService.IsContextPanelVisible;
        _commandService.Execute<ISetRegionVisibilityCommand>(command =>
        {
            command.Regions = LayoutRegion.Primary;
            command.IsVisible = isVisible;
        });
    }

    private void ToggleConsolePanelButton_Click(object sender, RoutedEventArgs e)
    {
        // Toggle panel visibility
        // Using an immediate command to ensure console is shown before focusing
        var isVisible = !_layoutService.IsConsolePanelVisible;
        _commandService.ExecuteImmediate<ISetRegionVisibilityCommand>(command =>
        {
            command.Regions = LayoutRegion.Console;
            command.IsVisible = isVisible;
        });

        // Request focus when showing the console
        if (isVisible)
        {
            var message = new RequestConsoleFocusMessage();
            _messengerService.Send(message);
        }
    }

    private void ToggleSecondaryPanelButton_Click(object sender, RoutedEventArgs e)
    {
        // Use command to toggle panel visibility
        var isVisible = !_layoutService.IsInspectorPanelVisible;
        _commandService.Execute<ISetRegionVisibilityCommand>(command =>
        {
            command.Regions = LayoutRegion.Secondary;
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

    private void LayoutModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUI)
        {
            return;
        }

        LayoutTransition transition;

        if (ReferenceEquals(sender, DefaultModeRadio))
        {
            transition = LayoutTransition.Default;
        }
        else if (ReferenceEquals(sender, FocusModeRadio))
        {
            transition = LayoutTransition.Focus;
        }
        else if (ReferenceEquals(sender, PresentationModeRadio))
        {
            transition = LayoutTransition.Presentation;
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

    private void FullScreenToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUI)
        {
            return;
        }

        _commandService.Execute<ISetLayoutCommand>(command =>
        {
            command.Transition = LayoutTransition.ToggleFullScreen;
        });
    }
}
