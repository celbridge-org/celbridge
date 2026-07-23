#if WINDOWS
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Minimum window size for the packaged WinAppSDK head, applied to the overlapped presenter. The
/// PreferredMinimum properties are absent from the Uno presenter, so this implementation is Windows-only.
/// </summary>
internal sealed class WinAppSdkWindowSizeConstraints : IWindowSizeConstraints
{
    public void ApplyMinimumSize(AppWindow appWindow, SizeInt32 minimumSize)
    {
        var presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter is null)
        {
            return;
        }

        presenter.PreferredMinimumWidth = minimumSize.Width;
        presenter.PreferredMinimumHeight = minimumSize.Height;
    }
}
#endif
