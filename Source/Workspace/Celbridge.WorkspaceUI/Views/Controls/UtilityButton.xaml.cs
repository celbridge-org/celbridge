using Celbridge.UserInterface;

namespace Celbridge.WorkspaceUI.Views.Controls;

/// <summary>
/// A single icon button in the Utility Panel rail. Mouse-driven and not focusable by design: clicking it
/// raises Click, and the selection indicator is driven by the bound IsSelected and IsFocused state, independent
/// of the click.
/// </summary>
public sealed partial class UtilityButton : UserControl
{
    // Icon opacity while the utility is docked, giving the button a disabled look while it stays clickable.
    private const double DockedIconOpacity = 0.4;

    public event EventHandler<RoutedEventArgs>? Click;

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected),
        typeof(bool),
        typeof(UtilityButton),
        new PropertyMetadata(false, OnSelectionStateChanged));

    public static readonly DependencyProperty IsFocusedProperty = DependencyProperty.Register(
        nameof(IsFocused),
        typeof(bool),
        typeof(UtilityButton),
        new PropertyMetadata(false, OnSelectionStateChanged));

    public static readonly DependencyProperty IsDockedProperty = DependencyProperty.Register(
        nameof(IsDocked),
        typeof(bool),
        typeof(UtilityButton),
        new PropertyMetadata(false, OnIsDockedChanged));

    public UtilityButton()
    {
        this.InitializeComponent();

        // Re-apply the visual state once loaded so the initial selection renders even if IsSelected was set
        // before the control entered the live visual tree.
        Loaded += (sender, e) => UpdateSelectionVisualState();
    }

    /// <summary>
    /// Shows the selection indicator when true. Reflects the currently shown surface, driven by the rail's
    /// selection state, not by the button's own click.
    /// </summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Colors the selection indicator to show whether the shown utility has focus: the accent color when true,
    /// a neutral grey when false. Only meaningful while IsSelected is true.
    /// </summary>
    public bool IsFocused
    {
        get => (bool)GetValue(IsFocusedProperty);
        set => SetValue(IsFocusedProperty, value);
    }

    /// <summary>
    /// Dims the button to a disabled-looking state while its utility is docked (presented in a document tab),
    /// reflecting that clicking it will not change the shown panel surface. The button stays interactive: a
    /// click activates the utility's document tab.
    /// </summary>
    public bool IsDocked
    {
        get => (bool)GetValue(IsDockedProperty);
        set => SetValue(IsDockedProperty, value);
    }

    public void SetIcon(IconSymbol symbol)
    {
        IconElement.Symbol = symbol;
    }

    public void SetIcon(string glyphName)
    {
        IconElement.GlyphName = glyphName;
    }

    public void SetTooltip(string tooltip)
    {
        ToolTipService.SetToolTip(ButtonElement, tooltip);
        ToolTipService.SetPlacement(ButtonElement, PlacementMode.Right);
        AutomationProperties.SetName(ButtonElement, tooltip);
    }

    public void SetAutomationId(string automationId)
    {
        AutomationProperties.SetAutomationId(ButtonElement, automationId);
    }

    private static void OnSelectionStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((UtilityButton)d).UpdateSelectionVisualState();
    }

    private static void OnIsDockedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var button = (UtilityButton)d;
        button.IconElement.Opacity = (bool)e.NewValue ? DockedIconOpacity : 1.0;
    }

    private void UpdateSelectionVisualState()
    {
        string state;
        if (!IsSelected)
        {
            state = "Unselected";
        }
        else if (IsFocused)
        {
            state = "SelectedFocused";
        }
        else
        {
            state = "SelectedUnfocused";
        }

        VisualStateManager.GoToState(this, state, false);
    }

    private void ButtonElement_Click(object sender, RoutedEventArgs e)
    {
        Click?.Invoke(this, e);
    }
}
