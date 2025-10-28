#if WINDOWS

using Celbridge.Settings;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Helper class to manage window maximized state persistence.
/// </summary>
internal sealed class WindowStateHelper
{
    private readonly IEditorSettings _editorSettings;
    private AppWindow? _appWindow;

    public WindowStateHelper(IEditorSettings editorSettings)
    {
        _editorSettings = editorSettings;
    }

    /// <summary>
    /// Initializes the helper with the main window and sets up state tracking.
    /// </summary>
    public void Initialize(Window mainWindow)
    {
        _appWindow = GetAppWindow(mainWindow);
        if (_appWindow == null)
        {
            return;
        }

        // Restore the saved maximized state
        if (_editorSettings.IsWindowMaximized)
        {
            var presenter = _appWindow.Presenter as OverlappedPresenter;
            presenter?.Maximize();
        }

        // Track window state changes
        _appWindow.Changed += OnAppWindowChanged;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        // Check if the window size or presenter changed
        if (args.DidSizeChange || args.DidPresenterChange)
        {
            var presenter = sender.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                _editorSettings.IsWindowMaximized = presenter.State == OverlappedPresenterState.Maximized;
            }
        }
    }

    private static AppWindow? GetAppWindow(Window? window)
    {
        if (window == null) return null;

        var windowHandle = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        return AppWindow.GetFromWindowId(windowId);
    }
}
#endif
