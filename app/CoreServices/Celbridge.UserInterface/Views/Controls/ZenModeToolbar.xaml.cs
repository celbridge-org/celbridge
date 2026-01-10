using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Settings;
using Celbridge.Workspace;
using Microsoft.UI.Xaml.Media.Animation;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// A minimal toolbar that appears at the top of the screen in Zen Mode when the mouse moves near the top edge.
/// Provides a simple "Exit Fullscreen" button to exit Zen Mode.
/// </summary>
public sealed partial class ZenModeToolbar : UserControl
{
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IEditorSettings _editorSettings;
    
    private readonly DispatcherTimer _hideTimer;
    
    private bool _isZenModeActive;
    private bool _isToolbarVisible;
    private bool _isMouseOverToolbar;
    
    private const double TRIGGER_ZONE_HEIGHT = 8; // pixels from top to trigger show
    private const double AUTO_HIDE_DELAY_SECONDS = 1.5; // seconds before auto-hiding
    private const double ANIMATION_DURATION_MS = 150; // animation speed
    private const double TOOLBAR_HEIGHT = 36; // height of the toolbar

    public ZenModeToolbar()
    {
        this.InitializeComponent();

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _editorSettings = ServiceLocator.AcquireService<IEditorSettings>();

        this.VerticalAlignment = VerticalAlignment.Top;
        this.HorizontalAlignment = HorizontalAlignment.Stretch;

        // Setup auto-hide timer
        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(AUTO_HIDE_DELAY_SECONDS)
        };
        _hideTimer.Tick += HideTimer_Tick;

        // Track mouse entering/leaving the toolbar
        ToolbarContainer.PointerEntered += ToolbarContainer_PointerEntered;
        ToolbarContainer.PointerExited += ToolbarContainer_PointerExited;

        Loaded += ZenModeToolbar_Loaded;
        Unloaded += ZenModeToolbar_Unloaded;
    }

    private void ZenModeToolbar_Loaded(object sender, RoutedEventArgs e)
    {
        // Register for Zen Mode changes
        _messengerService.Register<ZenModeChangedMessage>(this, OnZenModeChanged);
        
        // Check if already in Zen Mode
        _isZenModeActive = _editorSettings.IsZenModeActive;
    }

    private void ZenModeToolbar_Unloaded(object sender, RoutedEventArgs e)
    {
        _hideTimer.Stop();
        ToolbarContainer.PointerEntered -= ToolbarContainer_PointerEntered;
        ToolbarContainer.PointerExited -= ToolbarContainer_PointerExited;
        Loaded -= ZenModeToolbar_Loaded;
        Unloaded -= ZenModeToolbar_Unloaded;
        _messengerService.UnregisterAll(this);
    }

    private void OnZenModeChanged(object recipient, ZenModeChangedMessage message)
    {
        _isZenModeActive = message.IsZenModeActive;

        if (!_isZenModeActive)
        {
            // Exiting Zen Mode - hide toolbar immediately
            HideToolbar(animate: false);
        }
    }

    /// <summary>
    /// Called by the parent container when pointer moves. Checks if mouse is near top of screen.
    /// </summary>
    public void OnPointerMoved(double yPosition)
    {
        if (!_isZenModeActive)
        {
            return;
        }

        // Check if mouse is near the top of the window
        if (yPosition <= TRIGGER_ZONE_HEIGHT)
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
        if (_isZenModeActive && _isToolbarVisible)
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
        _hideTimer.Interval = TimeSpan.FromSeconds(AUTO_HIDE_DELAY_SECONDS);

        // Only hide if not hovering over toolbar and still in Zen Mode
        if (_isZenModeActive && !_isMouseOverToolbar)
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
            _hideTimer.Interval = TimeSpan.FromSeconds(AUTO_HIDE_DELAY_SECONDS);
            _hideTimer.Start();
            return;
        }

        _isToolbarVisible = true;
        ToolbarContainer.Visibility = Visibility.Visible;

        // Animate slide down
        var storyboard = new Storyboard();
        var slideAnimation = new DoubleAnimation
        {
            From = -TOOLBAR_HEIGHT,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(ANIMATION_DURATION_MS)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(slideAnimation, SlideTransform);
        Storyboard.SetTargetProperty(slideAnimation, "Y");
        storyboard.Children.Add(slideAnimation);
        storyboard.Begin();

        // Start hide timer
        _hideTimer.Interval = TimeSpan.FromSeconds(AUTO_HIDE_DELAY_SECONDS);
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

        if (animate)
        {
            // Animate slide up
            var storyboard = new Storyboard();
            var slideAnimation = new DoubleAnimation
            {
                From = 0,
                To = -TOOLBAR_HEIGHT,
                Duration = new Duration(TimeSpan.FromMilliseconds(ANIMATION_DURATION_MS)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            Storyboard.SetTarget(slideAnimation, SlideTransform);
            Storyboard.SetTargetProperty(slideAnimation, "Y");
            storyboard.Children.Add(slideAnimation);
            storyboard.Completed += (s, e) =>
            {
                ToolbarContainer.Visibility = Visibility.Collapsed;
            };
            storyboard.Begin();
        }
        else
        {
            SlideTransform.Y = -TOOLBAR_HEIGHT;
            ToolbarContainer.Visibility = Visibility.Collapsed;
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        _commandService.Execute<IToggleZenModeCommand>();
    }
}
