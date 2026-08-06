using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.WebHost;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Holds managed keyboard focus on a zero-sized element in the window root while a hosted web surface holds
/// native focus, so no managed control acts on the keys still routed through the managed tree.
/// </summary>
public class ManagedFocusSink : IManagedFocusSink
{
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IPlatformInfo _platformInfo;
    private readonly ILogger<ManagedFocusSink> _logger;

    private ContentControl? _sink;
    private bool _reportedFocusFailure;

    public ManagedFocusSink(
        IUserInterfaceService userInterfaceService,
        IPlatformInfo platformInfo,
        ILogger<ManagedFocusSink> logger)
    {
        _userInterfaceService = userInterfaceService;
        _platformInfo = platformInfo;
        _logger = logger;
    }

    public bool TakeFocus()
    {
        // Parking only means something where a web surface's native focus leaves managed focus behind. On
        // the other heads focusing the web view is itself a managed focus change, so there is nothing to
        // park and managed focus must stay free to move to the web view.
        if (!_platformInfo.HostedWebViewFocusIsNative)
        {
            return false;
        }

        var sink = _sink ??= CreateSink();
        if (sink is null)
        {
            return false;
        }

        // Focus is refused outright unless the sink is a tab stop, so it becomes one only for the moment it
        // takes focus: a zero-sized stop left in the tab order would strand a Tab press.
        sink.IsTabStop = true;
        var focused = sink.Focus(FocusState.Programmatic);
        sink.IsTabStop = false;

        if (!focused
            && !_reportedFocusFailure)
        {
            _reportedFocusFailure = true;
            _logger.LogWarning("Managed focus could not be parked, so keys may still reach the previously focused control");
        }

        return focused;
    }

    private ContentControl? CreateSink()
    {
        if (_userInterfaceService.MainWindow is not Window mainWindow
            || mainWindow.Content is not Panel rootPanel)
        {
            return null;
        }

        var sink = new ContentControl
        {
            Width = 0,
            Height = 0,
            IsTabStop = false
        };

        // The sink belongs to no panel, so without this the focus tracker would classify it as a move off
        // the workspace panels and clear panel focus. Focus landing here means nothing changed.
        FocusTracking.SetPreservePanelFocus(sink, true);

        rootPanel.Children.Add(sink);

        return sink;
    }
}
