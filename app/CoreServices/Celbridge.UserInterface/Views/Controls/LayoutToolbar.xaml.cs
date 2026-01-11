using Celbridge.Settings;
using Celbridge.Workspace;
using System.ComponentModel;

namespace Celbridge.UserInterface.Views;

public sealed partial class LayoutToolbar : UserControl
{
    private readonly IEditorSettings _editorSettings;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IStringLocalizer _stringLocalizer;

    private bool _isUpdatingUI = false;

    public LayoutToolbar()
    {
        InitializeComponent();

        _editorSettings = ServiceLocator.AcquireService<IEditorSettings>();
        _workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        Loaded += LayoutToolbar_Loaded;
        Unloaded += LayoutToolbar_Unloaded;
    }

    private void LayoutToolbar_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTooltips();
        ApplyLabels();
        UpdatePanelIcons();
        UpdateCheckBoxes();
        UpdateLayoutModeRadios();
        _editorSettings.PropertyChanged += EditorSettings_PropertyChanged;
    }

    private void LayoutToolbar_Unloaded(object sender, RoutedEventArgs e)
    {
        _editorSettings.PropertyChanged -= EditorSettings_PropertyChanged;
        Loaded -= LayoutToolbar_Loaded;
        Unloaded -= LayoutToolbar_Unloaded;
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
    }

    private void ApplyLabels()
    {
        ExplorerPanelLabel.Text = _stringLocalizer.GetString("LayoutToolbar_ExplorerPanelLabel");
        ConsolePanelLabel.Text = _stringLocalizer.GetString("LayoutToolbar_ConsolePanelLabel");
        InspectorPanelLabel.Text = _stringLocalizer.GetString("LayoutToolbar_InspectorPanelLabel");
        ResetLayoutButtonText.Text = _stringLocalizer.GetString("LayoutToolbar_ResetLayoutButton");

        LayoutModeHeader.Text = _stringLocalizer.GetString("LayoutToolbar_LayoutModeLabel");
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
                UpdateCheckBoxes();
                break;
            case nameof(IEditorSettings.WindowLayout):
                UpdatePanelIcons();
                UpdateCheckBoxes();
                UpdateLayoutModeRadios();
                break;
        }
    }

    private void UpdateCheckBoxes()
    {
        _isUpdatingUI = true;
        try
        {
            ExplorerPanelToggle.IsChecked = _editorSettings.IsContextPanelVisible;
            ConsolePanelToggle.IsChecked = _editorSettings.IsConsolePanelVisible;
            InspectorPanelToggle.IsChecked = _editorSettings.IsInspectorPanelVisible;
        }
        finally
        {
            _isUpdatingUI = false;
        }
    }

    private void UpdateLayoutModeRadios()
    {
        _isUpdatingUI = true;
        try
        {
            var windowLayout = _editorSettings.WindowLayout;
            WindowedModeRadio.IsChecked = windowLayout == WindowLayout.Windowed;
            FullScreenModeRadio.IsChecked = windowLayout == WindowLayout.FullScreen;
            ZenModeRadio.IsChecked = windowLayout == WindowLayout.ZenMode;
            PresenterModeRadio.IsChecked = windowLayout == WindowLayout.Presenter;
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
        
        // If user manually shows a panel while in a fullscreen mode that hides panels, exit to Windowed
        if (IsFullscreenModeWithHiddenPanels() && _editorSettings.IsContextPanelVisible)
        {
            SetWindowLayoutViaWorkspace(WindowLayout.Windowed);
        }
    }

    private void ToggleConsolePanelButton_Click(object sender, RoutedEventArgs e)
    {
        _editorSettings.IsConsolePanelVisible = !_editorSettings.IsConsolePanelVisible;
        
        // If user manually shows a panel while in a fullscreen mode that hides panels, exit to Windowed
        if (IsFullscreenModeWithHiddenPanels() && _editorSettings.IsConsolePanelVisible)
        {
            SetWindowLayoutViaWorkspace(WindowLayout.Windowed);
        }
    }

    private void ToggleInspectorPanelButton_Click(object sender, RoutedEventArgs e)
    {
        _editorSettings.IsInspectorPanelVisible = !_editorSettings.IsInspectorPanelVisible;
        
        // If user manually shows a panel while in a fullscreen mode that hides panels, exit to Windowed
        if (IsFullscreenModeWithHiddenPanels() && _editorSettings.IsInspectorPanelVisible)
        {
            SetWindowLayoutViaWorkspace(WindowLayout.Windowed);
        }
    }

    private bool IsFullscreenModeWithHiddenPanels()
    {
        var layout = _editorSettings.WindowLayout;
        return layout == WindowLayout.ZenMode || layout == WindowLayout.Presenter;
    }

    private void Button_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void PanelLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        PanelLayoutFlyout.ShowAt(PanelLayoutButton);
    }

    private void ExplorerPanelToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUI)
        {
            return;
        }

        if (ExplorerPanelToggle.IsChecked != _editorSettings.IsContextPanelVisible)
        {
            _editorSettings.IsContextPanelVisible = ExplorerPanelToggle.IsChecked == true;
            
            // If user manually shows a panel while in a fullscreen mode that hides panels, exit to Windowed
            if (IsFullscreenModeWithHiddenPanels() && ExplorerPanelToggle.IsChecked == true)
            {
                SetWindowLayoutViaWorkspace(WindowLayout.Windowed);
            }
        }
    }

    private void ConsolePanelToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUI)
        {
            return;
        }

        if (ConsolePanelToggle.IsChecked != _editorSettings.IsConsolePanelVisible)
        {
            _editorSettings.IsConsolePanelVisible = ConsolePanelToggle.IsChecked == true;
            
            // If user manually shows a panel while in a fullscreen mode that hides panels, exit to Windowed
            if (IsFullscreenModeWithHiddenPanels() && ConsolePanelToggle.IsChecked == true)
            {
                SetWindowLayoutViaWorkspace(WindowLayout.Windowed);
            }
        }
    }

    private void InspectorPanelToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUI)
        {
            return;
        }

        if (InspectorPanelToggle.IsChecked != _editorSettings.IsInspectorPanelVisible)
        {
            _editorSettings.IsInspectorPanelVisible = InspectorPanelToggle.IsChecked == true;
            
            // If user manually shows a panel while in a fullscreen mode that hides panels, exit to Windowed
            if (IsFullscreenModeWithHiddenPanels() && InspectorPanelToggle.IsChecked == true)
            {
                SetWindowLayoutViaWorkspace(WindowLayout.Windowed);
            }
        }
    }

    private void ResetLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        _editorSettings.ResetPanelState();
        
        // Also reset to Windowed mode
        if (_editorSettings.WindowLayout != WindowLayout.Windowed)
        {
            SetWindowLayoutViaWorkspace(WindowLayout.Windowed);
        }
        
        PanelLayoutFlyout.Hide();
    }

    private void LayoutModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUI)
        {
            return;
        }

        WindowLayout newLayout;
        
        if (ReferenceEquals(sender, WindowedModeRadio))
        {
            newLayout = WindowLayout.Windowed;
        }
        else if (ReferenceEquals(sender, FullScreenModeRadio))
        {
            newLayout = WindowLayout.FullScreen;
        }
        else if (ReferenceEquals(sender, ZenModeRadio))
        {
            newLayout = WindowLayout.ZenMode;
        }
        else if (ReferenceEquals(sender, PresenterModeRadio))
        {
            newLayout = WindowLayout.Presenter;
        }
        else
        {
            // Should never happen, but default to Windowed if sender is unexpected
            return;
        }

        if (_editorSettings.WindowLayout != newLayout)
        {
            SetWindowLayoutViaWorkspace(newLayout);
        }
    }

    private void SetWindowLayoutViaWorkspace(WindowLayout windowLayout)
    {
        if (_workspaceWrapper.IsWorkspacePageLoaded)
        {
            _workspaceWrapper.WorkspaceService.SetWindowLayout(windowLayout);
        }
    }
}
