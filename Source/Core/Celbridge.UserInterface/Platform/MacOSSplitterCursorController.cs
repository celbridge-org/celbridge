using Celbridge.UserInterface.Services;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Sets the native AppKit cursor for the panel splitters on macOS, driven from pointer enter and exit so the
/// resize cursor stays correct even after the pointer crosses a native editor web view, which reverts the OS
/// cursor without Uno's managed cursor path noticing.
/// </summary>
internal sealed class MacOSSplitterCursorController : ISplitterCursorController
{
    public void SetCursor(SplitterCursorShape shape)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var cursorSelectorName = shape switch
        {
            SplitterCursorShape.ResizeColumns => "resizeLeftRightCursor",
            SplitterCursorShape.ResizeRows => "resizeUpDownCursor",
            _ => "arrowCursor"
        };

        var cursorClass = GetClass("NSCursor");
        var cursor = SendMessage(cursorClass, GetSelector(cursorSelectorName));
        SendMessage(cursor, GetSelector("set"));
    }
}
