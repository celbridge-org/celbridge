using Celbridge.Commands;
using Microsoft.UI.Xaml.Media.Animation;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// A minimal toolbar that appears at the top of the screen in fullscreen modes when the mouse moves near the top edge.
/// </summary>
public sealed partial class FullscreenToolbar : UserControl
{
    private const double TriggerZoneHeight = 4; // pixels from top to trigger showing the toolbar
    private const double AutoHideDelay = 1.5; // seconds
    private const double AnimationDuration = 150; // ms

    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly ILayoutManager _layoutManager;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly DispatcherTimer _hideTimer;
    
    private bool _isFullscreenModeActive;
    private bool _isToolbarVisible;
    private bool _isMouseOverToolbar;
    private Storyboard? _currentAnimation;
    
    public string ExitFullscreenString => _stringLocalizer.GetString("FullScreenToolbar_ExitFullscreen");

    public FullscreenToolbar()
    {
        this.InitializeComponent();

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _layoutManager = ServiceLocator.AcquireService<ILayoutManager>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        this.DataContext = this;

        // Setup auto-hide timer
        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(AutoHideDelay)
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
        // Register for Window Mode changes
        _messengerService.Register<WindowModeChangedMessage>(this, OnWindowModeChanged);
        
        // Check if already in a fullscreen mode
        _isFullscreenModeActive = _layoutManager.IsFullScreen;
        
        // Update trigger zone visibility based on current mode
        UpdateTriggerZoneVisibility();
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

    private void OnWindowModeChanged(object recipient, WindowModeChangedMessage message)
    {
        _isFullscreenModeActive = message.WindowMode != WindowMode.Windowed;

        if (!_isFullscreenModeActive)
        {
            // Exiting fullscreen mode - hide toolbar immediately
            HideToolbar(animate: false);
        }
        
        // Update trigger zone visibility
        UpdateTriggerZoneVisibility();
    }
    
    private void UpdateTriggerZoneVisibility()
    {
        // Show the trigger zone only in fullscreen modes when the toolbar is not visible.
        TriggerZone.Visibility = _isFullscreenModeActive && !_isToolbarVisible 
            ? Visibility.Visible 
            : Visibility.Collapsed;
    }
    
    private void TriggerZone_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_isFullscreenModeActive)
        {
            ShowToolbar();
        }
    }

    /// <summary>
    /// Called by the parent container when pointer moves. Checks if mouse is near top of screen.
    /// </summary>
    public void OnPointerMoved(double yPosition)
    {
        if (!_isFullscreenModeActive)
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
        if (_isFullscreenModeActive && _isToolbarVisible)
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
        _hideTimer.Interval = TimeSpan.FromSeconds(AutoHideDelay);

        // Only hide if not hovering over toolbar and still in fullscreen mode
        if (_isFullscreenModeActive && !_isMouseOverToolbar)
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
            _hideTimer.Interval = TimeSpan.FromSeconds(AutoHideDelay);
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
        _hideTimer.Interval = TimeSpan.FromSeconds(AutoHideDelay);
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
        // Clicking anywhere on the toolbar exits fullscreen mode
        // This handles cases where other UI elements (e.g., screen sharing notifications) might block the exit button
        ExitFullscreen();
        e.Handled = true;
    }

    private void ExitFullscreen()
    {
        // Return to Windowed mode using SetLayoutCommand
        _commandService.Execute<ISetLayoutCommand>(command =>
        {
            command.Transition = WindowModeTransition.EnterWindowed;
        });
    }
}
