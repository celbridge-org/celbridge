using Celbridge.Logging;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Window bounds validator for the packaged WinAppSDK head, where DisplayArea reports the connected
/// displays. A saved placement is accepted when the title-bar strip intersects any display's work area.
/// </summary>
public sealed class WinAppSdkWindowBoundsValidator : IWindowBoundsValidator
{
    private readonly ILogger<WinAppSdkWindowBoundsValidator> _logger;

    public WinAppSdkWindowBoundsValidator(ILogger<WinAppSdkWindowBoundsValidator> logger)
    {
        _logger = logger;
    }

    public bool IsTitleBarVisible(RectInt32 windowBounds)
    {
        try
        {
            var titleBarRect = WindowPlacementPolicy.GetTitleBarStrip(windowBounds);

            var displayAreas = DisplayArea.FindAll();
            if (displayAreas == null || displayAreas.Count == 0)
            {
                return false;
            }

            // Indexing rather than foreach works around the exception described at
            // https://github.com/microsoft/microsoft-ui-xaml/issues/6454#issuecomment-2188377618
            for (int i = 0; i < displayAreas.Count; i++)
            {
                var displayArea = displayAreas[i];
                if (displayArea == null)
                {
                    continue;
                }

                if (WindowPlacementPolicy.IsTitleBarUsable(titleBarRect, displayArea.WorkArea))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception occurred while checking display areas");
            return false;
        }
    }
}
