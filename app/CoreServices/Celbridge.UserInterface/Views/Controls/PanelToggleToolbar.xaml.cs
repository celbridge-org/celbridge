using Celbridge.Commands;
using Celbridge.Settings;
using Celbridge.Workspace;
using Microsoft.UI.Xaml.Shapes;
using System.ComponentModel;

namespace Celbridge.UserInterface.Views;

public sealed partial class PanelToggleToolbar : UserControl
{
    private readonly IEditorSettings _editorSettings;
    private readonly ICommandService _commandService;
    private readonly IStringLocalizer _stringLocalizer;

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
        UpdatePanelIcons();
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

    private void EditorSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IEditorSettings.IsContextPanelVisible):
            case nameof(IEditorSettings.IsInspectorPanelVisible):
            case nameof(IEditorSettings.IsConsolePanelVisible):
                UpdatePanelIcons();
                break;
        }
    }

    private void UpdatePanelIcons()
    {
        // Update explorer panel icon
        ExplorerPanelFill.Visibility = _editorSettings.IsContextPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        ExplorerPanelDivider.Visibility = _editorSettings.IsContextPanelVisible ? Visibility.Collapsed : Visibility.Visible;

        // Update tools panel icon
        ConsolePanelFill.Visibility = _editorSettings.IsConsolePanelVisible ? Visibility.Visible : Visibility.Collapsed;
        ConsolePanelDivider.Visibility = _editorSettings.IsConsolePanelVisible ? Visibility.Collapsed : Visibility.Visible;

        // Update inspector panel icon
        InspectorPanelFill.Visibility = _editorSettings.IsInspectorPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        InspectorPanelDivider.Visibility = _editorSettings.IsInspectorPanelVisible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ToggleExplorerPanelButton_Click(object sender, RoutedEventArgs e)
    {
        _editorSettings.IsContextPanelVisible = !_editorSettings.IsContextPanelVisible;
    }

    private void ToggleConsolePanelButton_Click(object sender, RoutedEventArgs e)
    {
        _editorSettings.IsConsolePanelVisible = !_editorSettings.IsConsolePanelVisible;
    }

    private void ToggleInspectorPanelButton_Click(object sender, RoutedEventArgs e)
    {
        _editorSettings.IsInspectorPanelVisible = !_editorSettings.IsInspectorPanelVisible;
    }

    private void ToggleAllPanelsAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _commandService.Execute<IToggleAllPanelsCommand>();
        args.Handled = true;
    }

    private void Button_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
    }
}
