using Celbridge.Logging;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Constrains how small the user can resize the application window. macOS sets the constraint on the native
/// window, where AppKit enforces it in points. Every other head sets it on the overlapped presenter, which the
/// WinAppSDK and Uno presenters both enforce, in physical pixels.
/// </summary>
internal sealed class WindowSizeConstraints : IWindowSizeConstraints
{
    private readonly ILogger<WindowSizeConstraints> _logger;

    public WindowSizeConstraints(ILogger<WindowSizeConstraints> logger)
    {
        _logger = logger;
    }

    public void ApplyMinimumSize(AppWindow appWindow, SizeInt32 minimumSize)
    {
        if (OperatingSystem.IsMacOS())
        {
            MacOSWindowInterop.SetMinimumContentSize(minimumSize.Width, minimumSize.Height);

            _logger.LogDebug(
                "Applied minimum window size {Width} x {Height} to the native macOS window",
                minimumSize.Width,
                minimumSize.Height);

            return;
        }

        var presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter is null)
        {
            _logger.LogWarning(
                "Cannot apply a minimum window size: the presenter is {PresenterKind}, not an overlapped presenter",
                appWindow.Presenter.Kind);

            return;
        }

        presenter.PreferredMinimumWidth = minimumSize.Width;
        presenter.PreferredMinimumHeight = minimumSize.Height;

        _logger.LogDebug(
            "Applied minimum window size {Width} x {Height} to the overlapped presenter",
            minimumSize.Width,
            minimumSize.Height);
    }
}
