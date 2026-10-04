using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Microsoft.UI.Xaml.Media.Animation;

namespace Celbridge.WorkspaceUI.Views.Controls;

/// <summary>
/// A single icon button in the Utility Rail.
/// </summary>
public sealed partial class UtilityButton : UserControl
{
    // Whether the pointer is over the cell. Combines with the selection inputs to pick the visual state.
    private bool _isPointerOver;

    private Storyboard? _attentionStoryboard;
    private Storyboard? _indicatorStoryboard;

    // The tone a utility has marked this button with, and the tooltip it had before any state word was added.
    private UtilityIndicatorTone _indicatorTone = UtilityIndicatorTone.None;
    private string _baseTooltip = string.Empty;
    private string _baseIconName = string.Empty;

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

        // The swatches follow a theme change, and the glyph and fill take their new brushes here.
        ActualThemeChanged += (sender, e) => UpdateSelectionVisualState();
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
        _baseIconName = iconName;
        IconElement.IconName = iconName;
    }

    public void SetTooltip(string tooltip)
    {
        _baseTooltip = tooltip;
        ApplyTooltip(tooltip);
    }

    private void ApplyTooltip(string tooltip)
    {
        ToolTipService.SetToolTip(ButtonElement, tooltip);
        ToolTipService.SetPlacement(ButtonElement, PlacementMode.Right);
        AutomationProperties.SetName(ButtonElement, tooltip);
    }

    /// <summary>
    /// Marks the button with a tone set by the utility behind it, or clears the mark with None. The glyph takes
    /// the tone's colour; on a focused button the tone replaces the accent fill instead, with a white glyph,
    /// so the mark never disappears into the selection. A label is added to the tooltip, and an icon name
    /// replaces the glyph while the mark is on. Animate plays a pop and a flash in the tone when a mark is
    /// turned on.
    /// </summary>
    public void SetIndicator(UtilityIndicatorTone tone, string label, string iconName, bool animate)
    {
        var wasOn = _indicatorTone != UtilityIndicatorTone.None;
        _indicatorTone = tone;

        ApplyTooltip(string.IsNullOrEmpty(label) ? _baseTooltip : $"{_baseTooltip} ({label})");

        // Only a button showing its manifest icon has one to swap, which a built-in symbol button does not.
        if (!string.IsNullOrEmpty(_baseIconName))
        {
            IconElement.IconName = string.IsNullOrEmpty(iconName) || tone == UtilityIndicatorTone.None
                ? _baseIconName
                : iconName;
        }

        if (tone == UtilityIndicatorTone.None)
        {
            // The selection states set the glyph and fill through setters, which only apply on entering a
            // state. The local values are cleared and the state re-entered so they take effect again.
            IndicatorFill.Visibility = Visibility.Collapsed;
            IconElement.ClearValue(Microsoft.UI.Xaml.Controls.IconElement.ForegroundProperty);
            VisualStateManager.GoToState(this, "Unselected", false);
            UpdateSelectionVisualState();
            return;
        }

        UpdateSelectionVisualState();

        if (animate && !wasOn)
        {
            PlayIndicatorPop(tone);
        }
    }

    private Brush? GetToneBrush(UtilityIndicatorTone tone)
    {
        return tone switch
        {
            UtilityIndicatorTone.Danger => DangerSwatch.Background,
            UtilityIndicatorTone.Caution => CautionSwatch.Background,
            UtilityIndicatorTone.Success => SuccessSwatch.Background,
            UtilityIndicatorTone.Accent => AccentSwatch.Background,
            _ => null
        };
    }

    // Applied after every selection state change, because entering a state resets the glyph and fill to its setters.
    private void ApplyIndicatorColours(bool isSelectedFocused)
    {
        var brush = GetToneBrush(_indicatorTone);
        if (brush is null)
        {
            return;
        }

        IndicatorFill.Background = brush;
        IndicatorFill.Visibility = isSelectedFocused ? Visibility.Visible : Visibility.Collapsed;
        IconElement.Foreground = isSelectedFocused ? new SolidColorBrush(Microsoft.UI.Colors.White) : brush;
    }

    // A quick swell and settle of the glyph, with a flash of the tone behind it, so the moment a mark turns on
    // is hard to miss.
    private void PlayIndicatorPop(UtilityIndicatorTone tone)
    {
        _indicatorStoryboard?.Stop();

        var storyboard = new Storyboard();
        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            Storyboard.SetTarget(animation, IconScale);
            Storyboard.SetTargetProperty(animation, property);
            animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 1.0 });
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)),
                Value = 1.5,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)),
                Value = 0.88,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            });
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(460)),
                Value = 1.0,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
            storyboard.Children.Add(animation);
        }
        storyboard.Begin();
        _indicatorStoryboard = storyboard;

        var brush = GetToneBrush(tone);
        if (brush is not null)
        {
            AttentionOverlay.Background = brush;
        }
        _attentionStoryboard?.Stop();
        _attentionStoryboard = AttentionFlash.Play(AttentionOverlay);
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

    /// <summary>
    /// Briefly pulses the button with the accent color, then fades it back out.
    /// </summary>
    public void FlashAttention()
    {
        // The indicator pop borrows the overlay in its own tone, so the plain flash puts the accent back.
        AttentionOverlay.Background = AccentSwatch.Background;
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

        if (_indicatorTone != UtilityIndicatorTone.None)
        {
            ApplyIndicatorColours(state == "SelectedFocused");
        }
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
