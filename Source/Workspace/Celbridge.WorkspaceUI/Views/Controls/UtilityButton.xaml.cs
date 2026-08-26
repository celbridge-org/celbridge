using Celbridge.UserInterface;

namespace Celbridge.WorkspaceUI.Views.Controls;

/// <summary>
/// A single icon button in the Utility Rail.
/// </summary>
public sealed partial class UtilityButton : UserControl
{
    // Whether the pointer is over the cell. Combines with the selection inputs to pick the visual state.
    private bool _isPointerOver;

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

    public UtilityButton()
    {
        this.InitializeComponent();

        // Re-apply the visual state once loaded so the initial selection renders even if IsSelected was set
        // before the control entered the live visual tree.
        Loaded += (sender, e) => UpdateSelectionVisualState();
    }

    /// <summary>
    /// Fills the button with a neutral tone to show that the Utility Panel holds this utility.
    /// </summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Deepens the fill to the accent color to show that the keyboard is in the utility the panel is showing.
    /// A refinement of IsSelected, so it has no effect on its own.
    /// </summary>
    public bool IsFocused
    {
        get => (bool)GetValue(IsFocusedProperty);
        set => SetValue(IsFocusedProperty, value);
    }

    public void SetIcon(IconSymbol symbol)
    {
        IconElement.Symbol = symbol;
    }

    public void SetIcon(string iconName)
    {
        IconElement.IconName = iconName;
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

    /// <summary>
    /// Shows or hides the caution pip reporting that this surface has something the user should look at.
    /// </summary>
    public void SetIssuePipVisible(bool isVisible)
    {
        IssuePip.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void OnSelectionStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((UtilityButton)d).UpdateSelectionVisualState();
    }

    private void UpdateSelectionVisualState()
    {
        string state;
        if (IsSelected
            && IsFocused)
        {
            state = "SelectedFocused";
        }
        else if (IsSelected)
        {
            state = "SelectedUnfocused";
        }
        else if (_isPointerOver)
        {
            state = "UnselectedPointerOver";
        }
        else
        {
            state = "Unselected";
        }

        VisualStateManager.GoToState(this, state, false);
    }

    private void ButtonElement_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = true;
        UpdateSelectionVisualState();
    }

    private void ButtonElement_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = false;
        UpdateSelectionVisualState();
    }

    private void ButtonElement_Click(object sender, RoutedEventArgs e)
    {
        Click?.Invoke(this, e);
    }
}
