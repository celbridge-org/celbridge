using Celbridge.Commands;
using Celbridge.Platform;
using Microsoft.UI.Xaml.Media.Animation;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// A minimal strip shown at the top of the screen in Presentation mode, where the application toolbar is
/// hidden. On platforms without a native menu bar it is revealed whenever the mouse moves near the top
/// edge, and clicking it switches to the Focus layout. On platforms with a native menu bar the exit lives
/// in the Window menu instead, so the strip is a non-interactive hint that points there and is shown only
/// when Presentation mode is entered.
/// </summary>
public sealed partial class FullscreenToolbar : UserControl
{
    private const double TriggerZoneHeight = 4; // pixels from top to trigger showing the toolbar
    private const double AutoHideDelay = 1.5; // seconds
    private const double HintAutoHideDelay = 5; // seconds; the hint is read rather than clicked, so it lingers
    private const double AnimationDuration = 150; // ms

    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IWindowModeService _windowModeService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly DispatcherTimer _hideTimer;
    private readonly TimeSpan _autoHideDelay;

    // The strip only tells the user where the exit is, rather than acting as the exit itself.
    private readonly bool _isHintOnly;

    private bool _isToolbarHidden;
    private bool _isToolbarVisible;
    private bool _isMouseOverToolbar;
    private Storyboard? _currentAnimation;

    public string ExitPresentationString
    {
        get
        {
            var stringKey = _isHintOnly
                ? "FullScreenToolbar_ExitPresentationHint"
                : "FullScreenToolbar_ExitPresentation";

            return _stringLocalizer.GetString(stringKey);
        }
    }

    public FullscreenToolbar()
    {
        this.InitializeComponent();

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _windowModeService = ServiceLocator.AcquireService<IWindowModeService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        _isHintOnly = platformInfo.UsesNativeMenuBar;

        if (_isHintOnly)
        {
            // A display-only overlay never has to win pointer input from the native views it covers.
            IsHitTestVisible = false;
        }

        this.DataContext = this;

        var autoHideSeconds = _isHintOnly
            ? HintAutoHideDelay
            : AutoHideDelay;
        _autoHideDelay = TimeSpan.FromSeconds(autoHideSeconds);

        // Setup auto-hide timer
        _hideTimer = new DispatcherTimer
        {
            Interval = _autoHideDelay
        };
        _hideTimer.Tick += HideTimer_Tick;

        // Track mouse entering/leaving the toolbar
        ToolbarContainer.PointerEntered += ToolbarContainer_PointerEntered;
        ToolbarContainer.PointerExited += ToolbarContainer_PointerExited;
        
        // Track mouse entering the trigger zone (this works even over WebView2)
        TriggerZone.PointerEntered += TriggerZone_PointerEntered;

        Loaded += FullscreenToolbar_Loaded;
        Unloaded += FullscreenToolbar_Unloaded;
    }

    private void FullscreenToolbar_Loaded(object sender, RoutedEventArgs e)
    {
        // Register for layout mode changes
        _messengerService.Register<LayoutModeChangedMessage>(this, OnLayoutModeChanged);

        // The toolbar is hidden only in Presentation mode
        _isToolbarHidden = _windowModeService.LayoutMode == LayoutMode.Presentation;

        // Update trigger zone visibility based on current mode
        UpdateTriggerZoneVisibility();

        if (_isToolbarHidden)
        {
            // The app started up already in Presentation mode, so briefly reveal the strip to show the
            // user how to exit.
            ShowToolbar();
        }
    }

    private void FullscreenToolbar_Unloaded(object sender, RoutedEventArgs e)
    {
        _hideTimer.Stop();
        ToolbarContainer.PointerEntered -= ToolbarContainer_PointerEntered;
        ToolbarContainer.PointerExited -= ToolbarContainer_PointerExited;
        TriggerZone.PointerEntered -= TriggerZone_PointerEntered;
        Loaded -= FullscreenToolbar_Loaded;
        Unloaded -= FullscreenToolbar_Unloaded;
        _messengerService.UnregisterAll(this);
    }

    private void OnLayoutModeChanged(object recipient, LayoutModeChangedMessage message)
    {
        _isToolbarHidden = message.LayoutMode == LayoutMode.Presentation;

        if (_isToolbarHidden)
        {
            // Entering Presentation hides the application toolbar. Briefly reveal the strip so the user
            // always sees how to exit, then let the auto-hide timer slide it away.
            ShowToolbar();
        }
        else
        {
            // The application toolbar is visible again, so hide the reveal strip immediately.
            HideToolbar(animate: false);
        }

        // Update trigger zone visibility
        UpdateTriggerZoneVisibility();
    }

    private void UpdateTriggerZoneVisibility()
    {
        if (_isHintOnly)
        {
            // The hint is shown only when Presentation mode is entered, so there is nothing to trigger.
            TriggerZone.Visibility = Visibility.Collapsed;
            return;
        }

        // Show the trigger zone only when the application toolbar is hidden and the reveal strip is not
        // already showing.
        TriggerZone.Visibility = _isToolbarHidden && !_isToolbarVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void TriggerZone_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_isToolbarHidden)
        {
            ShowToolbar();
        }
    }

    /// <summary>
    /// Called by the parent container when pointer moves. Checks if mouse is near top of screen.
    /// </summary>
    public void OnPointerMoved(double yPosition)
    {
        if (_isHintOnly ||
            !_isToolbarHidden)
        {
            return;
        }

        // Check if mouse is near the top of the window
        if (yPosition <= TriggerZoneHeight)
        {
            ShowToolbar();
        }
    }

    private void ToolbarContainer_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isMouseOverToolbar = true;
        // Stop the hide timer while mouse is over toolbar
        _hideTimer.Stop();
    }

    private void ToolbarContainer_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isMouseOverToolbar = false;
        // Hide immediately when mouse leaves toolbar (short delay for smooth UX)
        if (_isToolbarHidden && _isToolbarVisible)
        {
            _hideTimer.Stop();
            // Use a very short delay so it doesn't feel jarring
            _hideTimer.Interval = TimeSpan.FromMilliseconds(300);
            _hideTimer.Start();
        }
    }

    private void HideTimer_Tick(object? sender, object e)
    {
        _hideTimer.Stop();
        // Reset interval to normal
        _hideTimer.Interval = _autoHideDelay;

        // Only hide if not hovering over toolbar and still in fullscreen mode
        if (_isToolbarHidden && !_isMouseOverToolbar)
        {
            HideToolbar(animate: true);
        }
    }

    private void ShowToolbar()
    {
        if (_isToolbarVisible)
        {
            // Reset hide timer
            _hideTimer.Stop();
            _hideTimer.Interval = _autoHideDelay;
            _hideTimer.Start();
            return;
        }

        // Stop any running animation before starting a new one
        if (_currentAnimation != null)
        {
            _currentAnimation.Stop();
            _currentAnimation = null;
        }

        _isToolbarVisible = true;
        ToolbarContainer.Visibility = Visibility.Visible;
        
        // Hide the trigger zone while toolbar is visible (toolbar handles its own events)
        TriggerZone.Visibility = Visibility.Collapsed;

        // Animate slide down from current position
        var storyboard = new Storyboard();
        var slideAnimation = new DoubleAnimation
        {
            From = SlideTransform.Y,  // Start from current position in case animation was interrupted
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(AnimationDuration)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(slideAnimation, SlideTransform);
        Storyboard.SetTargetProperty(slideAnimation, "Y");
        storyboard.Children.Add(slideAnimation);
        
        // Track the current animation
        _currentAnimation = storyboard;
        storyboard.Completed += (s, e) => 
        {
            if (_currentAnimation == storyboard)
            {
                _currentAnimation = null;
            }
        };
        
        storyboard.Begin();

        // Start hide timer
        _hideTimer.Interval = _autoHideDelay;
        _hideTimer.Start();
    }

    private void HideToolbar(bool animate)
    {
        if (!_isToolbarVisible && ToolbarContainer.Visibility == Visibility.Collapsed)
        {
            return;
        }

        _hideTimer.Stop();
        _isToolbarVisible = false;

        // Stop any running animation before starting a new one
        if (_currentAnimation != null)
        {
            _currentAnimation.Stop();
            _currentAnimation = null;
        }

        if (animate)
        {
            // Animate slide up from current position
            var storyboard = new Storyboard();
            var slideAnimation = new DoubleAnimation
            {
                From = SlideTransform.Y,  // Start from current position in case animation was interrupted
                To = -ToolbarContainer.Height,
                Duration = new Duration(TimeSpan.FromMilliseconds(AnimationDuration)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            Storyboard.SetTarget(slideAnimation, SlideTransform);
            Storyboard.SetTargetProperty(slideAnimation, "Y");
            storyboard.Children.Add(slideAnimation);
            
            // Track the current animation
            _currentAnimation = storyboard;
            storyboard.Completed += (s, e) =>
            {
                if (_currentAnimation == storyboard)
                {
                    _currentAnimation = null;
                }
                ToolbarContainer.Visibility = Visibility.Collapsed;
                // Show trigger zone again after toolbar is hidden
                UpdateTriggerZoneVisibility();
            };
            
            storyboard.Begin();
        }
        else
        {
            SlideTransform.Y = -ToolbarContainer.Height;
            ToolbarContainer.Visibility = Visibility.Collapsed;
            // Show trigger zone again after toolbar is hidden
            UpdateTriggerZoneVisibility();
        }
    }

    private void ToolbarContainer_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Clicking anywhere on the strip reveals the application toolbar. Clicking anywhere (not just a
        // button) handles cases where other UI elements (e.g. screen-sharing notifications) overlap it.
        RevealMainToolbar();
        e.Handled = true;
    }

    private void RevealMainToolbar()
    {
        // Switch to the Focus layout, which shows the application toolbar while keeping the side panels
        // hidden. The user can then change layout or fullscreen from the toolbar itself.
        _commandService.Execute<ISetLayoutCommand>(command =>
        {
            command.Transition = LayoutTransition.Focus;
        });
    }
}
