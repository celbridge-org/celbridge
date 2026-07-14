using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// No-op splitter cursor controller for the packaged Windows head and any non-macOS Skia head, where the
/// managed cursor already applies the splitter cursor reliably and needs no native override.
/// </summary>
internal sealed class DefaultSplitterCursorController : ISplitterCursorController
{
    public void SetCursor(SplitterCursorShape shape)
    {
        // The managed ProtectedCursor already applies the splitter cursor reliably on these heads.
    }
}
