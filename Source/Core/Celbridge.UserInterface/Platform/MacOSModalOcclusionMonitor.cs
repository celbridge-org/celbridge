using System.Runtime.InteropServices;
using Celbridge.Logging;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Hides hosted native WebViews while a modal dialog is open on the Uno Skia macOS head. Uno occludes
/// native views under a modal by detaching them or masking them with a window-wide clip, but a
/// detach/reattach cycle clears the masks without invalidating Uno's cached clip path, and a clip update
/// fails outright while any attached subview has an empty frame, so WebViews can render over the dialog.
/// Hidden is the state Uno intends for a native view under a modal, so each dialog scope enforces it
/// directly: it hides every visible native subview when the dialog opens, sweeps for late attaches while
/// the dialog is open, and restores the hidden views when the dialog closes.
/// </summary>
internal static class MacOSModalOcclusionMonitor
{
    /// <summary>
    /// Starts WebView occlusion for one modal dialog. Dispose the returned scope when the dialog closes
    /// to restore the hidden views. No-op off macOS.
    /// </summary>
    public static IDisposable BeginDialogScope(string dialogName)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return NoOpScope.Instance;
        }

        // No DI logger means no UI host to monitor (e.g. unit tests).
        var logger = ServiceLocator.ServiceProvider?.GetService(typeof(ILogger<DialogScope>)) as ILogger<DialogScope>;
        if (logger is null)
        {
            return NoOpScope.Instance;
        }

        var scope = new DialogScope(dialogName, logger);
        scope.Start();
        return scope;
    }

    private sealed class NoOpScope : IDisposable
    {
        public static readonly NoOpScope Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed class DialogScope : IDisposable
    {
        // Bounds how long a WebView attached mid-dialog can render over the dialog before it is hidden.
        private static readonly TimeSpan SweepInterval = TimeSpan.FromMilliseconds(250);

        private readonly ILogger _logger;
        private readonly string _dialogName;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly List<IntPtr> _hiddenSubviews = [];

        public DialogScope(string dialogName, ILogger logger)
        {
            _logger = logger;
            _dialogName = dialogName;
        }

        public void Start()
        {
            _ = RunSweepsAsync();
        }

        private async Task RunSweepsAsync()
        {
            // The awaits capture the UI SynchronizationContext (BeginDialogScope is called from the dialog
            // service on the UI thread), so the AppKit calls below stay on the main thread.
            try
            {
                var cancellationToken = _cancellationTokenSource.Token;
                while (!cancellationToken.IsCancellationRequested)
                {
                    HideVisibleNativeSubviews();
                    await Task.Delay(SweepInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Modal WebView occlusion sweep failed");
            }
        }

        private void HideVisibleNativeSubviews()
        {
            var window = MacOSWindowInterop.GetMainWindow();
            if (window == IntPtr.Zero)
            {
                return;
            }

            var contentViewController = SendMessage(window, GetSelector("contentViewController"));
            if (contentViewController == IntPtr.Zero)
            {
                return;
            }

            var hostView = SendMessage(contentViewController, GetSelector("view"));
            if (hostView == IntPtr.Zero)
            {
                return;
            }

            // The content view controller's view holds exactly the native elements Uno hosts (WebViews);
            // the Skia canvas lives elsewhere in the window.
            var subviewArray = SendMessage(hostView, GetSelector("subviews"));
            if (subviewArray == IntPtr.Zero)
            {
                return;
            }

            var subviewCount = SendMessageReturnNint(subviewArray, GetSelector("count"));
            var hiddenThisSweep = 0;

            var objectAtIndexSelector = GetSelector("objectAtIndex:");
            for (nint index = 0; index < subviewCount; index++)
            {
                var subview = SendMessage(subviewArray, objectAtIndexSelector, index);
                if (subview == IntPtr.Zero)
                {
                    continue;
                }

                var hidden = SendMessageReturnBool(subview, GetSelector("isHidden"));
                var alpha = SendMessageReturnDouble(subview, GetSelector("alphaValue"));
                var frame = SendMessageReturnCGRect(subview, GetSelector("frame"));

                var isVisible = !hidden &&
                    alpha > 0 &&
                    frame.Size.Width > 0 &&
                    frame.Size.Height > 0;
                if (!isVisible)
                {
                    continue;
                }

                // Retain so the handle stays valid for the restore on dispose, even if Uno disposes the
                // native element while the dialog is open.
                SendMessage(subview, GetSelector("retain"));
                SendMessageVoidBool(subview, GetSelector("setHidden:"), true);
                _hiddenSubviews.Add(subview);
                hiddenThisSweep++;
            }

            if (hiddenThisSweep > 0)
            {
                _logger.LogDebug(
                    "Hid {Count} native subview(s) while the modal dialog '{DialogName}' is open",
                    hiddenThisSweep,
                    _dialogName);
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();

            if (_hiddenSubviews.Count == 0)
            {
                return;
            }

            foreach (var subview in _hiddenSubviews)
            {
                SendMessageVoidBool(subview, GetSelector("setHidden:"), false);
                SendMessageVoidNoArguments(subview, GetSelector("release"));
            }

            _logger.LogDebug(
                "Restored {Count} hidden native subview(s) after the modal dialog '{DialogName}' closed",
                _hiddenSubviews.Count,
                _dialogName);
            _hiddenSubviews.Clear();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGSize
    {
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public CGPoint Origin;
        public CGSize Size;
    }

    // NSRect is a homogeneous aggregate of four doubles, so the ARM64 ABI returns it in the floating
    // point registers. The struct-by-value return keeps this declaration local rather than in the shared
    // runtime.
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern CGRect SendMessageReturnCGRect(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendMessageVoidBool(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.I1)] bool argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendMessageVoidNoArguments(IntPtr receiver, IntPtr selector);
}
