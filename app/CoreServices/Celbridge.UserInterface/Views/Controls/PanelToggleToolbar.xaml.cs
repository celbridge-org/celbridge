using Celbridge.Commands;
using Celbridge.Settings;
using Celbridge.Workspace;
using System.ComponentModel;

namespace Celbridge.UserInterface.Views;

public sealed partial class PanelToggleToolbar : UserControl
{
    private readonly IEditorSettings _editorSettings;
    private readonly ICommandService _commandService;
    private readonly IStringLocalizer _stringLocalizer;
    private bool _isUpdatingCheckboxes = false;

    public PanelToggleToolbar()
    {
        InitializeComponent();

        _editorSettings = ServiceLocator.AcquireService<IEditorSettings>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        Loaded += PanelToggleToolbar_Loaded;
        Unloaded += PanelToggleToolbar_Unloaded;
    }

    private void PanelToggleToolbar_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTooltips();
        ApplyLabels();
        UpdatePanelIcons();
        UpdateCheckBoxes();
        _editorSettings.PropertyChanged += EditorSettings_PropertyChanged;
    }

    private void PanelToggleToolbar_Unloaded(object sender, RoutedEventArgs e)
    {
        _editorSettings.PropertyChanged -= EditorSettings_PropertyChanged;
        Loaded -= PanelToggleToolbar_Loaded;
        Unloaded -= PanelToggleToolbar_Unloaded;
    }

    private void ApplyTooltips()
    {
        var layoutTooltip = _stringLocalizer.GetString("PanelToolbar_CustomizeLayoutTooltip");
        ToolTipService.SetToolTip(PanelLayoutButton, layoutTooltip);
        ToolTipService.SetPlacement(PanelLayoutButton, PlacementMode.Bottom);

        var explorerTooltip = _stringLocalizer.GetString("PanelToolbar_ToggleExplorerTooltip");
        ToolTipService.SetToolTip(ToggleExplorerPanelButton, explorerTooltip);
        ToolTipService.SetPlacement(ToggleExplorerPanelButton, PlacementMode.Bottom);

        var consoleTooltip = _stringLocalizer.GetString("PanelToolbar_ToggleConsoleTooltip");
        ToolTipService.SetToolTip(ToggleConsolePanelButton, consoleTooltip);
        ToolTipService.SetPlacement(ToggleConsolePanelButton, PlacementMode.Bottom);

        var inspectorTooltip = _stringLocalizer.GetString("PanelToolbar_ToggleInspectorTooltip");
        ToolTipService.SetToolTip(ToggleInspectorPanelButton, inspectorTooltip);
        ToolTipService.SetPlacement(ToggleInspectorPanelButton, PlacementMode.Bottom);
    }

    private void ApplyLabels()
    {
        ExplorerPanelLabel.Text = _stringLocalizer.GetString("PanelToolbar_ExplorerPanelLabel");
        ConsolePanelLabel.Text = _stringLocalizer.GetString("PanelToolbar_ConsolePanelLabel");
        InspectorPanelLabel.Text = _stringLocalizer.GetString("PanelToolbar_InspectorPanelLabel");
        ZenModeLabel.Text = _stringLocalizer.GetString("PanelToolbar_ZenModeLabel");
        ResetLayoutButtonText.Text = _stringLocalizer.GetString("PanelToolbar_ResetLayoutButton");
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
            case nameof(IEditorSettings.IsZenModeActive):
                UpdatePanelIcons();
                UpdateCheckBoxes();
                break;
        }
    }

    private void UpdateCheckBoxes()
    {
        _isUpdatingCheckboxes = true;
        try
        {
            ExplorerPanelToggle.IsChecked = _editorSettings.IsContextPanelVisible;
            ConsolePanelToggle.IsChecked = _editorSettings.IsConsolePanelVisible;
            InspectorPanelToggle.IsChecked = _editorSettings.IsInspectorPanelVisible;
            UpdateZenModeCheckbox();
        }
        finally
        {
            _isUpdatingCheckboxes = false;
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
        
        // If user manually shows a panel while in Zen Mode, exit Zen Mode
        if (_editorSettings.IsZenModeActive && _editorSettings.IsContextPanelVisible)
        {
            _editorSettings.IsZenModeActive = false;
        }
    }

    private void ToggleConsolePanelButton_Click(object sender, RoutedEventArgs e)
    {
        _editorSettings.IsConsolePanelVisible = !_editorSettings.IsConsolePanelVisible;
        
        // If user manually shows a panel while in Zen Mode, exit Zen Mode
        if (_editorSettings.IsZenModeActive && _editorSettings.IsConsolePanelVisible)
        {
            _editorSettings.IsZenModeActive = false;
        }
    }

    private void ToggleInspectorPanelButton_Click(object sender, RoutedEventArgs e)
    {
        _editorSettings.IsInspectorPanelVisible = !_editorSettings.IsInspectorPanelVisible;
        
        // If user manually shows a panel while in Zen Mode, exit Zen Mode
        if (_editorSettings.IsZenModeActive && _editorSettings.IsInspectorPanelVisible)
        {
            _editorSettings.IsZenModeActive = false;
        }
    }

    private void ToggleAllPanelsAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _commandService.Execute<IToggleZenModeCommand>();
        args.Handled = true;
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
        if (_isUpdatingCheckboxes)
        {
            return;
        }

        if (ExplorerPanelToggle.IsChecked != _editorSettings.IsContextPanelVisible)
        {
            _editorSettings.IsContextPanelVisible = ExplorerPanelToggle.IsChecked == true;
            
            // If user manually shows a panel while in Zen Mode, exit Zen Mode
            if (_editorSettings.IsZenModeActive && ExplorerPanelToggle.IsChecked == true)
            {
                _editorSettings.IsZenModeActive = false;
            }
        }
    }

    private void ConsolePanelToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingCheckboxes)
        {
            return;
        }

        if (ConsolePanelToggle.IsChecked != _editorSettings.IsConsolePanelVisible)
        {
            _editorSettings.IsConsolePanelVisible = ConsolePanelToggle.IsChecked == true;
            
            // If user manually shows a panel while in Zen Mode, exit Zen Mode
            if (_editorSettings.IsZenModeActive && ConsolePanelToggle.IsChecked == true)
            {
                _editorSettings.IsZenModeActive = false;
            }
        }
    }

    private void InspectorPanelToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingCheckboxes)
        {
            return;
        }

        if (InspectorPanelToggle.IsChecked != _editorSettings.IsInspectorPanelVisible)
        {
            _editorSettings.IsInspectorPanelVisible = InspectorPanelToggle.IsChecked == true;
            
            // If user manually shows a panel while in Zen Mode, exit Zen Mode
            if (_editorSettings.IsZenModeActive && InspectorPanelToggle.IsChecked == true)
            {
                _editorSettings.IsZenModeActive = false;
            }
        }
    }

    private void ResetLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        _editorSettings.ResetPanelLayout();
        PanelLayoutFlyout.Hide();
    }

    private void ZenModeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingCheckboxes)
        {
            return;
        }

        _commandService.Execute<IToggleZenModeCommand>();
    }

    private void UpdateZenModeCheckbox()
    {
        // Zen mode checkbox is checked only when explicitly in Zen Mode
        ZenModeToggle.IsChecked = _editorSettings.IsZenModeActive;
    }
}
