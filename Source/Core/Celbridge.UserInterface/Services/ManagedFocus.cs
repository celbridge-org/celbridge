using Celbridge.Logging;
using Celbridge.Platform;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Managed keyboard focus for the window. Focus is given up by moving it onto an inert zero-sized
/// placeholder in the window root, because WinUI has no way to express that no control has focus.
/// </summary>
public class ManagedFocus : IManagedFocus
{
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IPlatformInfo _platformInfo;
    private readonly ILogger<ManagedFocus> _logger;

    private ContentControl? _placeholder;
    private bool _reportedFocusFailure;

    public ManagedFocus(
        IUserInterfaceService userInterfaceService,
        IPlatformInfo platformInfo,
        ILogger<ManagedFocus> logger)
    {
        _userInterfaceService = userInterfaceService;
        _platformInfo = platformInfo;
        _logger = logger;
    }

    public UIElement? FocusedElement
    {
        get
        {
            if (_userInterfaceService.MainWindow is not Window mainWindow
                || mainWindow.Content is not UIElement rootContent
                || rootContent.XamlRoot is null)
            {
                return null;
            }

            return Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(rootContent.XamlRoot) as UIElement;
        }
    }

    public FocusLocation FocusLocation
    {
        get
        {
            var focusedElement = FocusedElement;

            // Nothing focused is nothing stranded, so it reads as the main content.
            return focusedElement is null
                ? FocusLocation.MainContent
                : FocusTracking.GetFocusLocation(focusedElement);
        }
    }

    public INotedFocus NoteFocus()
    {
        return new NotedFocus(this, FocusedElement);
    }

    public void Yield()
    {
        // Yielding only means something where a web surface's native focus leaves managed focus behind. On
        // the other heads focusing the web view is itself a managed focus change, so there is nothing to
        // yield and managed focus must stay free to move to the web view.
        if (!_platformInfo.HostedWebViewFocusIsNative)
        {
            return;
        }

        var placeholder = _placeholder ??= CreatePlaceholder();
        if (placeholder is null)
        {
            return;
        }

        // Re-applying managed focus the placeholder already holds makes Uno resign the web surface's
        // native focus again, which the first responder monitor reconciles by yielding again, looping.
        if (ReferenceEquals(FocusedElement, placeholder))
        {
            return;
        }

        // Focus is refused outright unless the placeholder is a tab stop, so it becomes one only for the
        // moment it takes focus: a zero-sized stop left in the tab order would strand a Tab press.
        // Moving managed focus makes Uno resign the native first responder, so the page holding the caret
        // sees a blur here. Logged because that blur is indistinguishable, at the page, from the user
        // clicking away.
        _logger.LogTrace("Yielding managed focus to the placeholder");

        placeholder.IsTabStop = true;
        var focused = placeholder.Focus(FocusState.Programmatic);
        placeholder.IsTabStop = false;

        if (!focused
            && !_reportedFocusFailure)
        {
            _reportedFocusFailure = true;
            _logger.LogWarning("Managed focus could not be yielded, so keys may still reach the previously focused control");
        }
    }

    private ContentControl? CreatePlaceholder()
    {
        if (_userInterfaceService.MainWindow is not Window mainWindow
            || mainWindow.Content is not Panel rootPanel)
        {
            return null;
        }

        var placeholder = new ContentControl
        {
            Width = 0,
            Height = 0,
            IsTabStop = false
        };

        // The placeholder belongs to no panel, so without this the focus tracker would classify it as a move
        // off the workspace panels and clear panel focus. Focus landing here means nothing changed.
        FocusTracking.SetPreservePanelFocus(placeholder, true);

        rootPanel.Children.Add(placeholder);

        return placeholder;
    }

    private sealed class NotedFocus : INotedFocus
    {
        private readonly ManagedFocus _managedFocus;
        private readonly UIElement? _element;

        public NotedFocus(ManagedFocus managedFocus, UIElement? element)
        {
            _managedFocus = managedFocus;
            _element = element;
        }

        public bool IsFocusBack => _element is not null
            && ReferenceEquals(_managedFocus.FocusedElement, _element);

        public bool TryReturnFocus()
        {
            // An element in a popup that has since closed, or one gone from the window, cannot take the
            // keyboard back.
            if (_element is not Control control
                || FocusTracking.GetFocusLocation(control) != FocusLocation.MainContent)
            {
                return false;
            }

            return control.Focus(FocusState.Programmatic);
        }
    }
}
