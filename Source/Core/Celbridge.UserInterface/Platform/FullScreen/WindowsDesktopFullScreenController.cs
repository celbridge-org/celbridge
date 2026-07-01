using System.Diagnostics;
using System.Runtime.InteropServices;
using Celbridge.Logging;

namespace Celbridge.UserInterface.Platform.FullScreen;

/// <summary>
/// Fullscreen controller for the Windows Skia desktop head. The cross-platform DisplayArea APIs are
/// not implemented on the WPF shell, so the monitor bounds and the taskbar-covering topmost state are
/// applied with Win32 calls against the native window handle.
/// </summary>
public sealed class WindowsDesktopFullScreenController : DesktopFullScreenControllerBase
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly IntPtr HwndTopmost = new IntPtr(-1);
    private static readonly IntPtr HwndNoTopmost = new IntPtr(-2);

    public WindowsDesktopFullScreenController(ILogger<WindowsDesktopFullScreenController> logger)
        : base(logger)
    {
    }

    protected override bool TryCoverMonitor()
    {
        var windowHandle = Process.GetCurrentProcess().MainWindowHandle;
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        var monitorInfo = new MONITORINFO
        {
            cbSize = Marshal.SizeOf<MONITORINFO>()
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        // rcMonitor is the full monitor rectangle in physical pixels, including the taskbar. Sizing
        // the window to it directly (rather than via AppWindow, whose coordinate unit is ambiguous on
        // the WPF shell) keeps everything in physical pixels, and HWND_TOPMOST covers the taskbar.
        var bounds = monitorInfo.rcMonitor;
        SetWindowPos(
            windowHandle,
            HwndTopmost,
            bounds.Left,
            bounds.Top,
            bounds.Right - bounds.Left,
            bounds.Bottom - bounds.Top,
            SwpFrameChanged);

        return true;
    }

    protected override void ReleaseMonitorCover()
    {
        var windowHandle = Process.GetCurrentProcess().MainWindowHandle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(
            windowHandle,
            HwndNoTopmost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
