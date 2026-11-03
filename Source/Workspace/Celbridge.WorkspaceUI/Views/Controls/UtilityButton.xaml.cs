using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Media.Animation;

namespace Celbridge.WorkspaceUI.Views.Controls;

/// <summary>
/// A single icon button in the Utility Rail.
/// </summary>
public sealed partial class UtilityButton : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    // Whether the pointer is over the cell. Combines with the selection inputs to pick the visual state.
    private bool _isPointerOver;

    // The tooltip that names the button's item, and the label of the button's pip. The tooltip on screen joins
    // the two.
    private string _tooltip = string.Empty;
    private string _pipLabel = string.Empty;

    private Storyboard? _attentionStoryboard;

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
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

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

    /// <summary>
    /// Sets the tooltip that names the button's item. The tooltip is also the button's accessible name.
    /// </summary>
    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;

        ToolTipService.SetPlacement(ButtonElement, PlacementMode.Right);
        AutomationProperties.SetName(ButtonElement, tooltip);

        ApplyTooltip();
    }

    public void SetAutomationId(string automationId)
    {
        AutomationProperties.SetAutomationId(ButtonElement, automationId);
    }

    /// <summary>
    /// Shows the status pip in the color for the kind, or hides it for a null kind. A label adds a line to the
    /// tooltip and becomes the button's help text. A pulsing pip fades out and back in every few seconds.
    /// </summary>
    public void SetPip(PipKind? kind, string label, bool isPulsing)
    {
        PipElement.Kind = kind;
        PipElement.IsPulsing = isPulsing;

        _pipLabel = kind is null ? string.Empty : label;
        AutomationProperties.SetHelpText(ButtonElement, _pipLabel);

        ApplyTooltip();
    }

    private void ApplyTooltip()
    {
        var tooltip = _tooltip;
        if (!string.IsNullOrEmpty(_pipLabel))
        {
            tooltip = _stringLocalizer.GetString("StatusPip_Tooltip_WithLabel", _tooltip, _pipLabel);
        }

        ToolTipService.SetToolTip(ButtonElement, tooltip);
    }

    /// <summary>
    /// Briefly pulses the button with the accent color, then fades it back out.
    /// </summary>
    public void FlashAttention()
    {
        _attentionStoryboard?.Stop();
        _attentionStoryboard = AttentionFlash.Play(AttentionOverlay);
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
