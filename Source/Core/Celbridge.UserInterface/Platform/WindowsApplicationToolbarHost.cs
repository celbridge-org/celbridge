#if WINDOWS
using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Hosts the application toolbar inside the custom title bar on the packaged Windows head, extending it
/// into the window's title-bar area.
/// </summary>
internal sealed class WindowsApplicationToolbarHost : IApplicationToolbarHost
{
    public UserControl Install(Window window, Panel layoutRoot)
    {
        var titleBar = new TitleBar();
        layoutRoot.Children.Add(titleBar);

        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(titleBar);

        // The standard caption button height, which the toolbar strip is sized to match.
        var appWindow = window.AppWindow;
        if (appWindow?.TitleBar != null)
        {
            appWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Standard;
        }

        return titleBar;
    }
}
#endif
