using System.Runtime.InteropServices;
using System.Text;
using Celbridge.Logging;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Detects hosted native WebViews rendering on top of an open modal dialog on the Uno Skia macOS head.
/// Uno occludes native views under a modal by detaching fully covered views and masking the rest with a
/// window-wide CAShapeLayer clip, but a detach/reattach cycle clears the masks without invalidating Uno's
/// cached clip path, so the masks are never reapplied and the dialog renders behind the WebViews. Each
/// dialog scope polls the native subview state while the dialog is open and logs a diagnostic dump when
/// the occlusion invariant is violated. Detection and logging only; no native state is modified.
/// </summary>
internal static class MacOSModalOcclusionMonitor
{
    /// <summary>
    /// Starts occlusion monitoring for one modal dialog. Dispose the returned scope when the dialog
    /// closes. No-op off macOS.
    /// </summary>
    public static IDisposable BeginDialogScope(string dialogName)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return NoOpScope.Instance;
        }

        var scope = new DialogScope(dialogName);
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
        // The initial delay lets the dialog entrance animation finish, so a healthy occlusion pass has
        // settled before the first check. Later checks catch masks lost while the dialog stays open.
        private static readonly TimeSpan InitialCheckDelay = TimeSpan.FromMilliseconds(600);
        private static readonly TimeSpan RepeatCheckInterval = TimeSpan.FromSeconds(2);

        private readonly ILogger _logger;
        private readonly string _dialogName;
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        public DialogScope(string dialogName)
        {
            _logger = ServiceLocator.AcquireService<ILogger<DialogScope>>();
            _dialogName = dialogName;
        }

        public void Start()
        {
            _ = RunChecksAsync();
        }

        private async Task RunChecksAsync()
        {
            // The awaits capture the UI SynchronizationContext (BeginDialogScope is called from the dialog
            // service on the UI thread), so the AppKit calls below stay on the main thread.
            try
            {
                var cancellationToken = _cancellationTokenSource.Token;
                await Task.Delay(InitialCheckDelay, cancellationToken);

                var checkNumber = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    checkNumber++;
                    var reported = CheckOcclusionInvariant(checkNumber);
                    if (reported)
                    {
                        // One diagnostic dump per dialog is enough to characterise the failure.
                        return;
                    }

                    await Task.Delay(RepeatCheckInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Modal occlusion diagnostic check failed");
            }
        }

        /// <summary>
        /// Inspects the native subviews hosting WebViews and logs a report when any of them can render
        /// over the open dialog. Returns true when a report was logged.
        /// </summary>
        private bool CheckOcclusionInvariant(int checkNumber)
        {
            var window = MacOSWindowInterop.GetMainWindow();
            if (window == IntPtr.Zero)
            {
                return false;
            }

            var contentViewController = SendMessage(window, GetSelector("contentViewController"));
            if (contentViewController == IntPtr.Zero)
            {
                return false;
            }

            var hostView = SendMessage(contentViewController, GetSelector("view"));
            if (hostView == IntPtr.Zero)
            {
                return false;
            }

            var subviewArray = SendMessage(hostView, GetSelector("subviews"));
            if (subviewArray == IntPtr.Zero)
            {
                return false;
            }

            var subviewCount = SendMessageReturnNint(subviewArray, GetSelector("count"));
            if (subviewCount == 0)
            {
                // Healthy steady state: Uno detaches every native view that a modal fully covers.
                return false;
            }

            // Dialogs are centered in the window, so the host view center is inside the dialog area. A
            // visible native subview whose frame contains the center and whose mask does not exclude it
            // is rendering over the dialog.
            var hostBounds = SendMessageReturnCGRect(hostView, GetSelector("bounds"));
            var centerX = hostBounds.Size.Width / 2;
            var centerY = hostBounds.Size.Height / 2;

            var subviewLines = new List<string>((int)subviewCount);
            var dialogObscured = false;
            var visibleSubviewPresent = false;

            var objectAtIndexSelector = GetSelector("objectAtIndex:");
            for (nint index = 0; index < subviewCount; index++)
            {
                var subview = SendMessage(subviewArray, objectAtIndexSelector, index);
                if (subview == IntPtr.Zero)
                {
                    continue;
                }

                var className = GetClassName(subview);
                var frame = SendMessageReturnCGRect(subview, GetSelector("frame"));
                var hidden = SendMessageReturnBool(subview, GetSelector("isHidden"));
                var alpha = SendMessageReturnDouble(subview, GetSelector("alphaValue"));

                var layer = SendMessage(subview, GetSelector("layer"));
                var mask = layer == IntPtr.Zero ? IntPtr.Zero : SendMessage(layer, GetSelector("mask"));
                var maskPath = mask == IntPtr.Zero ? IntPtr.Zero : SendMessage(mask, GetSelector("path"));

                string maskStatus;
                if (mask == IntPtr.Zero)
                {
                    maskStatus = "none";
                }
                else
                {
                    var maskClassName = GetClassName(mask);
                    maskStatus = maskPath == IntPtr.Zero ? $"{maskClassName}(no path)" : $"{maskClassName}(path)";
                }

                var isVisible = !hidden &&
                    alpha > 0 &&
                    frame.Size.Width > 0 &&
                    frame.Size.Height > 0;
                visibleSubviewPresent |= isVisible;

                var containsCenter = isVisible &&
                    centerX >= frame.Origin.X &&
                    centerX <= frame.Origin.X + frame.Size.Width &&
                    centerY >= frame.Origin.Y &&
                    centerY <= frame.Origin.Y + frame.Size.Height;

                bool? maskExcludesCenter = null;
                if (containsCenter)
                {
                    if (maskPath == IntPtr.Zero)
                    {
                        maskExcludesCenter = false;
                    }
                    else
                    {
                        // The mask path is in view-local points with the even-odd fill rule, matching how
                        // Uno builds the CAShapeLayer clip.
                        var centerInView = new CGPoint
                        {
                            X = centerX - frame.Origin.X,
                            Y = centerY - frame.Origin.Y,
                        };
                        var maskContainsCenter = CGPathContainsPoint(maskPath, IntPtr.Zero, centerInView, eoFill: true);
                        maskExcludesCenter = !maskContainsCenter;
                    }

                    if (maskExcludesCenter == false)
                    {
                        dialogObscured = true;
                    }
                }

                var maskCenterStatus = maskExcludesCenter switch
                {
                    true => "mask excludes dialog center",
                    false => "MASK DOES NOT EXCLUDE DIALOG CENTER",
                    null => "does not cover dialog center",
                };

                subviewLines.Add(
                    $"  [{index}] {className} frame=({frame.Origin.X:F0},{frame.Origin.Y:F0} {frame.Size.Width:F0}x{frame.Size.Height:F0}) " +
                    $"hidden={hidden} alpha={alpha:F2} mask={maskStatus} ({maskCenterStatus})");
            }

            if (!visibleSubviewPresent)
            {
                return false;
            }

            var report = new StringBuilder();
            report.AppendLine(
                $"Host bounds {hostBounds.Size.Width:F0}x{hostBounds.Size.Height:F0}, check #{checkNumber}, {subviewCount} native subview(s):");
            foreach (var line in subviewLines)
            {
                report.AppendLine(line);
            }

            if (dialogObscured)
            {
                _logger.LogError(
                    "Native WebView content is obscuring the open modal dialog '{DialogName}' (Uno clip-mask desync). {Report}",
                    _dialogName,
                    report.ToString());
            }
            else
            {
                _logger.LogWarning(
                    "Unexpected visible native subview(s) while the modal dialog '{DialogName}' is open; Uno normally detaches occluded native views. {Report}",
                    _dialogName,
                    report.ToString());
            }

            return true;
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        }
    }

    private const string CoreGraphicsFramework = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

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

    [DllImport(CoreGraphicsFramework)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGPathContainsPoint(
        IntPtr path,
        IntPtr transform,
        CGPoint point,
        [MarshalAs(UnmanagedType.I1)] bool eoFill);
}
