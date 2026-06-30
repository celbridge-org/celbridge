using Celbridge.Logging;
using Celbridge.UserInterface.Helpers;
using Windows.Graphics;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Window bounds validator for the Skia desktop heads, where DisplayArea is unavailable. On macOS the
/// saved placement is validated against native NSScreen geometry; on the other Skia heads the placement
/// cannot be validated, so it is treated as not visible and the caller uses the default placement.
/// </summary>
public sealed class SkiaWindowBoundsValidator : IWindowBoundsValidator
{
    private const int TitleBarHeight = 40;

    private readonly ILogger<SkiaWindowBoundsValidator> _logger;

    public SkiaWindowBoundsValidator(ILogger<SkiaWindowBoundsValidator> logger)
    {
        _logger = logger;
    }

    public bool IsTitleBarVisible(RectInt32 windowBounds)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        if (!MacOSWindowInterop.TryGetScreens(out var screens))
        {
            _logger.LogDebug("Could not read native screen geometry; using default window placement");
            return false;
        }

        // The saved bounds come from AppWindow, whose coordinate unit on the Skia head is ambiguous
        // (points or physical pixels). NSScreen frames are in points with a bottom-left origin, so each
        // screen is converted to a top-left rect and the saved title-bar strip is tested against it in
        // both point space and pixel space. A hit in either accepts the restore, which keeps the unit
        // ambiguity from silently rejecting a valid placement. The values are logged so the
        // interpretation can be tightened once confirmed on device.
        var titleBarRect = new RectInt32
        {
            X = windowBounds.X,
            Y = windowBounds.Y,
            Width = windowBounds.Width,
            Height = TitleBarHeight
        };

        // The flip from a bottom-left to a top-left origin is relative to the primary display (the one
        // whose origin is at 0,0), falling back to the first screen.
        double primaryHeightPoints = screens[0].FrameHeight;
        foreach (var screen in screens)
        {
            if (screen.FrameX == 0
                && screen.FrameY == 0)
            {
                primaryHeightPoints = screen.FrameHeight;
                break;
            }
        }

        foreach (var screen in screens)
        {
            double topLeftYPoints = primaryHeightPoints - (screen.FrameY + screen.FrameHeight);
            double scale = screen.BackingScaleFactor <= 0 ? 1.0 : screen.BackingScaleFactor;

            var pointRect = new RectInt32
            {
                X = (int)screen.FrameX,
                Y = (int)topLeftYPoints,
                Width = (int)screen.FrameWidth,
                Height = (int)screen.FrameHeight
            };

            var pixelRect = new RectInt32
            {
                X = (int)(screen.FrameX * scale),
                Y = (int)(topLeftYPoints * scale),
                Width = (int)(screen.FrameWidth * scale),
                Height = (int)(screen.FrameHeight * scale)
            };

            _logger.LogDebug(
                "Window restore check: saved=({SavedX},{SavedY},{SavedW},{SavedH}) " +
                "screenPoints=({PointX},{PointY},{PointW},{PointH}) " +
                "screenPixels=({PixelX},{PixelY},{PixelW},{PixelH}) scale={Scale}",
                windowBounds.X, windowBounds.Y, windowBounds.Width, windowBounds.Height,
                pointRect.X, pointRect.Y, pointRect.Width, pointRect.Height,
                pixelRect.X, pixelRect.Y, pixelRect.Width, pixelRect.Height,
                scale);

            if (titleBarRect.IntersectsWith(pointRect)
                || titleBarRect.IntersectsWith(pixelRect))
            {
                return true;
            }
        }

        return false;
    }
}
