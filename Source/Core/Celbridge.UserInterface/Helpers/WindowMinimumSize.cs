using Celbridge.UserInterface.Views;
using Celbridge.Workspace;
using Windows.Foundation;
using Windows.Graphics;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Composes the smallest size the application window may be resized to. The authored size is the budget the
/// workspace layout floors are chosen to fit inside, and the composed floor of the layout a workspace opens
/// with sits beneath it, so a floor raised past the budget grows the window rather than clipping the
/// workspace.
/// </summary>
public static class WindowMinimumSize
{
    /// <summary>
    /// The smallest window width the application is usable in, in device-independent pixels. Authored rather
    /// than derived from the layout, because the application toolbar needs most of this width for its own
    /// content and composes no minimum of its own, and because the settings dialog is sized to sit inside
    /// this with the application still visible around it.
    /// </summary>
    public const int AuthoredWidth = 1080;

    /// <summary>
    /// The smallest window height the application is usable in, in device-independent pixels. Held short of
    /// the width's reference so the window still fits a 1366x768 display with its taskbar.
    /// </summary>
    public const int AuthoredHeight = 680;

    /// <summary>
    /// The window frame either side of the application content. Counted against the minimum window size, which
    /// covers the whole window, while the composed minimum covers only the workspace inside it.
    /// </summary>
    public const int WindowFrameWidth = 16;

    /// <summary>
    /// The window frame below the application content, and the native title bar above it on the heads that draw
    /// one. Neither is a size the layout can measure, so the allowance is authored to cover the largest of them.
    /// </summary>
    public const int WindowFrameHeight = 40;

    /// <summary>
    /// The smallest window the layout a workspace opens with can be laid out in: its composed floor, the
    /// application toolbar above it, and the window's own chrome around both. A layout the user has widened
    /// or split needs more, and is clamped or clipped rather than holding the window open.
    /// </summary>
    public static Size ComposeDefaultLayoutWindow(IReadOnlySet<WorkspaceArea> visibleAreas, double gutterSize)
    {
        var workspaceMinimumSize = WorkspaceMinimumSize.ComposeDefaultLayout(visibleAreas, gutterSize);

        double width = workspaceMinimumSize.Width + WindowFrameWidth;
        double height = workspaceMinimumSize.Height +
            ApplicationToolbar.ToolbarHeight +
            WindowFrameHeight;

        return new Size(width, height);
    }

    /// <summary>
    /// The minimum window size to apply, in the unit the head measures its window in. The authored size is
    /// held to at least what the default layout needs, then scaled: the composition is in device-independent
    /// pixels, which is not what every head measures its window in.
    /// </summary>
    public static SizeInt32 Compose(IReadOnlySet<WorkspaceArea> visibleAreas, double gutterSize, double windowSizeScale)
    {
        var defaultLayoutWindow = ComposeDefaultLayoutWindow(visibleAreas, gutterSize);

        double width = Math.Max(AuthoredWidth, defaultLayoutWindow.Width);
        double height = Math.Max(AuthoredHeight, defaultLayoutWindow.Height);

        return new SizeInt32
        {
            Width = (int)Math.Ceiling(width * windowSizeScale),
            Height = (int)Math.Ceiling(height * windowSizeScale)
        };
    }
}
