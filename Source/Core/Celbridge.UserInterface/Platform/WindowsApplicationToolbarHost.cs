#if WINDOWS
using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Hosts the application toolbar inside the custom title bar on the packaged Windows head, extending it
/// into the window's title-bar area.
/// </summary>
internal sealed class WindowsApplicationToolbarHost : IApplicationToolbarHost
{
    public ITitleBar Install(Window window, Panel layoutRoot)
    {
        var titleBar = new TitleBar();
        layoutRoot.Children.Add(titleBar);

        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(titleBar);

        // Taller caption buttons (48px instead of 32px) so the system minimize/maximize/close buttons
        // match the increased title-bar height.
        var appWindow = window.AppWindow;
        if (appWindow?.TitleBar != null)
        {
            appWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
        }

        return titleBar;
    }
}
#endif
