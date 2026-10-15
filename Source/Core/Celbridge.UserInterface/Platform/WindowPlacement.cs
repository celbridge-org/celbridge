using Celbridge.Logging;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Reads and sets the application window's bounds, and places a window that has no usable saved bounds. On macOS
/// the AppWindow's move and resize have no effect. The native window stays at AppKit's creation size, in the
/// corner of the screen. macOS therefore works on the native window's frame in points. Every other head works on
/// the AppWindow, and keeps the placement the system gives a new window.
/// </summary>
internal sealed class WindowPlacement : IWindowPlacement
{
    private readonly ILogger<WindowPlacement> _logger;

    public WindowPlacement(ILogger<WindowPlacement> logger)
    {
        _logger = logger;
    }

    public RectInt32 GetBounds(AppWindow appWindow)
    {
        if (OperatingSystem.IsMacOS()
            && MacOSWindowInterop.TryGetMainWindowFrame(out var frame))
        {
            return frame;
        }

        return new RectInt32
        {
            X = appWindow.Position.X,
            Y = appWindow.Position.Y,
            Width = appWindow.Size.Width,
            Height = appWindow.Size.Height
        };
    }

    public void Restore(AppWindow appWindow, RectInt32 bounds)
    {
        if (!OperatingSystem.IsMacOS())
        {
            appWindow.MoveAndResize(bounds);
            return;
        }

        if (!MacOSWindowInterop.TryGetWorkAreas(out var workAreas))
        {
            _logger.LogDebug("Could not read the screens' work areas; keeping the system's window placement");
            return;
        }

        // Saved bounds can be larger than the screen, or lie partly off the screen.
        var frame = WindowPlacementPolicy.FitInWorkArea(bounds, workAreas);
        MacOSWindowInterop.SetMainWindowFrame(frame);
        LogPlacement("Restored", frame);
    }

    public void ApplyDefault(AppWindow appWindow, SizeInt32 minimumSize)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        if (!MacOSWindowInterop.TryGetMainWindowWorkArea(out var workArea))
        {
            _logger.LogDebug("Could not read the screen's work area; keeping the system's window placement");
            return;
        }

        var frame = WindowPlacementPolicy.ComposeDefaultBounds(workArea, minimumSize);
        MacOSWindowInterop.SetMainWindowFrame(frame);
        LogPlacement("Placed", frame);
    }

    private void LogPlacement(string verb, RectInt32 frame)
    {
        _logger.LogDebug(
            "{Verb} the window at ({X},{Y},{Width},{Height})",
            verb,
            frame.X,
            frame.Y,
            frame.Width,
            frame.Height);
    }
}
