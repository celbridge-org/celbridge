using Celbridge.Settings;
using System.ComponentModel;

namespace Celbridge.UserInterface.Views;

public sealed partial class LayoutToolbar : UserControl
{
    private readonly IEditorSettings _editorSettings;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;

    private bool _isUpdatingUI = false;
    private bool _isOnWorkspacePage = false;

    public LayoutToolbar()
    {
        InitializeComponent();

        _editorSettings = ServiceLocator.AcquireService<IEditorSettings>();
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

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
        _editorSettings.PropertyChanged += EditorSettings_PropertyChanged;
        _messengerService.Register<ActivePageChangedMessage>(this, OnActivePageChanged);
    }

    private void LayoutToolbar_Unloaded(object sender, RoutedEventArgs e)
    {
        _editorSettings.PropertyChanged -= EditorSettings_PropertyChanged;
        _messengerService.UnregisterAll(this);
        Loaded -= LayoutToolbar_Loaded;
        Unloaded -= LayoutToolbar_Unloaded;
    }

    private void OnActivePageChanged(object recipient, ActivePageChangedMessage message)
    {
        _isOnWorkspacePage = message.ActivePage == ApplicationPage.Workspace;
        UpdatePanelToggleVisibility();
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

    private void EditorSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IEditorSettings.IsContextPanelVisible):
            case nameof(IEditorSettings.IsInspectorPanelVisible):
            case nameof(IEditorSettings.IsConsolePanelVisible):
            case nameof(IEditorSettings.ContextPanelWidth):
            case nameof(IEditorSettings.InspectorPanelWidth):
            case nameof(IEditorSettings.ConsolePanelHeight):
                UpdatePanelIcons();
                break;
            case nameof(IEditorSettings.WindowMode):
                UpdatePanelIcons();
                UpdateWindowModeRadios();
                break;
        }
    }

    private void UpdateWindowModeRadios()
    {
        _isUpdatingUI = true;
        try
        {
            var windowMode = _editorSettings.WindowMode;
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
        ExplorerPanelIcon.IsActivePanel = _editorSettings.IsContextPanelVisible;
        ConsolePanelIcon.IsActivePanel = _editorSettings.IsConsolePanelVisible;
        InspectorPanelIcon.IsActivePanel = _editorSettings.IsInspectorPanelVisible;
    }

    private void ToggleExplorerPanelButton_Click(object sender, RoutedEventArgs e)
    {
        _editorSettings.IsContextPanelVisible = !_editorSettings.IsContextPanelVisible;
        UpdateWindowLayoutBasedOnPanelState();
    }

    private void ToggleConsolePanelButton_Click(object sender, RoutedEventArgs e)
    {
        _editorSettings.IsConsolePanelVisible = !_editorSettings.IsConsolePanelVisible;
        UpdateWindowLayoutBasedOnPanelState();
    }

    private void ToggleInspectorPanelButton_Click(object sender, RoutedEventArgs e)
    {
        _editorSettings.IsInspectorPanelVisible = !_editorSettings.IsInspectorPanelVisible;
        UpdateWindowLayoutBasedOnPanelState();
    }

    /// <summary>
    /// Updates the window mode based on current panel visibility state.
    /// - In Zen Mode: showing any panel switches to FullScreen
    /// - In FullScreen or Zen Mode: hiding all panels switches to Zen Mode
    /// </summary>
    private void UpdateWindowLayoutBasedOnPanelState()
    {
        var currentMode = _editorSettings.WindowMode;
        bool anyPanelVisible = _editorSettings.IsContextPanelVisible || 
                               _editorSettings.IsInspectorPanelVisible || 
                               _editorSettings.IsConsolePanelVisible;

        if (currentMode == WindowMode.ZenMode && anyPanelVisible)
        {
            // In Zen Mode and showing a panel: switch to FullScreen
            _userInterfaceService.SetWindowMode(WindowMode.FullScreen);
        }
        else if (currentMode == WindowMode.FullScreen && !anyPanelVisible)
        {
            // In FullScreen and all panels hidden: switch to Zen Mode
            _userInterfaceService.SetWindowMode(WindowMode.ZenMode);
        }
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
        _editorSettings.ResetPanelState();
        
        // Also reset to Windowed mode
        if (_editorSettings.WindowMode != WindowMode.Windowed)
        {
            _userInterfaceService.SetWindowMode(WindowMode.Windowed);
        }
        
        PanelLayoutFlyout.Hide();
    }

    private void WindowModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUI)
        {
            return;
        }

        WindowMode newMode;
        
        if (ReferenceEquals(sender, WindowedModeRadio))
        {
            newMode = WindowMode.Windowed;
        }
        else if (ReferenceEquals(sender, FullScreenModeRadio))
        {
            newMode = WindowMode.FullScreen;
        }
        else if (ReferenceEquals(sender, ZenModeRadio))
        {
            newMode = WindowMode.ZenMode;
        }
        else if (ReferenceEquals(sender, PresenterModeRadio))
        {
            newMode = WindowMode.Presenter;
        }
        else
        {
            // Should never happen, but default to Windowed if sender is unexpected
            return;
        }

        if (_editorSettings.WindowMode != newMode)
        {
            _userInterfaceService.SetWindowMode(newMode);
        }
    }
}
