using System.Runtime.InteropServices;
using Celbridge.UserInterface;

namespace Celbridge.Automation.Platform;

/// <summary>
/// Reads the minimize, maximize and close buttons that the Windows App SDK draws in the main window's title bar.
/// Frames are in device-independent pixels, measured from the top left of the window's client area. Call this on
/// the UI thread.
/// </summary>
internal class WindowsNativeControlReader : INativeControlReader
{
    // The class of the child window that draws the caption buttons. The buttons sit side by side and are equally
    // wide.
    private const string CaptionControlsClassName = "ReunionWindowingCaptionControls";

    private const int StyleIndex = -16;
    private const int MinimizeBoxStyle = 0x00020000;
    private const int MaximizeBoxStyle = 0x00010000;

    // WM_SYSCOMMAND, and the command that each caption button sends when clicked.
    private const uint SystemCommandMessage = 0x0112;
    private const nint MinimizeCommand = 0xF020;
    private const nint MaximizeCommand = 0xF030;
    private const nint RestoreCommand = 0xF120;
    private const nint CloseCommand = 0xF060;

    // The DPI at 100% scaling, where one device-independent pixel is one physical pixel.
    private const double StandardDpi = 96;

    private record CaptionButton(string AutomationId, nint Command, bool IsEnabled);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private readonly IUserInterfaceService _userInterfaceService;

    public WindowsNativeControlReader(IUserInterfaceService userInterfaceService)
    {
        _userInterfaceService = userInterfaceService;
    }

    public IReadOnlyList<ShowingControl> ReadControls()
    {
        var controls = new List<ShowingControl>();
        if (_userInterfaceService.MainWindow is Window mainWindow)
        {
            var window = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
            ReadCaptionButtons(window, controls);
        }

        return controls;
    }

    public NativeView? FindNativeView(FrameworkElement element)
    {
        // WinUI draws a WebView2's page inside the control itself, so the layout already gives the right frame.
        return null;
    }

    private static void ReadCaptionButtons(IntPtr window, List<ShowingControl> controls)
    {
        // When the main window is minimized, the caption button window still exists but sits far off screen.
        var captionControls = FindWindowEx(window, IntPtr.Zero, CaptionControlsClassName, null);
        if (captionControls == IntPtr.Zero ||
            IsIconic(window) ||
            !IsWindowVisible(captionControls) ||
            !GetWindowRect(captionControls, out var captionRect))
        {
            return;
        }

        var clientOrigin = new NativePoint();
        ClientToScreen(window, ref clientOrigin);
        var scale = GetDpiForWindow(window) / StandardDpi;

        var buttons = DescribeCaptionButtons(window);
        var left = (captionRect.Left - clientOrigin.X) / scale;
        var top = (captionRect.Top - clientOrigin.Y) / scale;
        var width = (captionRect.Right - captionRect.Left) / scale / buttons.Count;
        var height = (captionRect.Bottom - captionRect.Top) / scale;

        for (int index = 0; index < buttons.Count; index++)
        {
            var button = buttons[index];
            var bounds = new ControlBounds(left + index * width, top, width, height);

            // Match UI Automation, which names these buttons by their English automation IDs and gives no class
            // name.
            var info = new ControlInfo(
                button.AutomationId,
                button.AutomationId,
                "Button",
                string.Empty,
                bounds,
                button.IsEnabled,
                null,
                null);

            controls.Add(new ShowingControl(info, () => PressCaptionButton(window, button.Command)));
        }
    }

    // The window style determines which caption buttons are active. If the window is disabled, none of them are.
    private static List<CaptionButton> DescribeCaptionButtons(IntPtr window)
    {
        var isEnabled = IsWindowEnabled(window);
        var style = GetWindowLong(window, StyleIndex);
        var canMinimize = isEnabled && (style & MinimizeBoxStyle) != 0;
        var canMaximize = isEnabled && (style & MaximizeBoxStyle) != 0;

        var maximizeButton = new CaptionButton("Maximize", MaximizeCommand, canMaximize);
        if (IsZoomed(window))
        {
            maximizeButton = new CaptionButton("Restore", RestoreCommand, canMaximize);
        }

        var buttons = new List<CaptionButton>
        {
            new("Minimize", MinimizeCommand, canMinimize),
            maximizeButton,
            new("Close", CloseCommand, isEnabled)
        };

        return buttons;
    }

    // Post rather than send, so the window handles the command after the automation call returns, as it would
    // after a real click.
    private static ControlAction? PressCaptionButton(IntPtr window, nint command)
    {
        if (!PostMessage(window, SystemCommandMessage, command, 0))
        {
            return null;
        }

        return ControlAction.Invoke;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, nint wParam, nint lParam);
}
