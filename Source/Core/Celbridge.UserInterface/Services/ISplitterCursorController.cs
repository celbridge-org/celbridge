namespace Celbridge.UserInterface.Services;

/// <summary>
/// The resize cursor a splitter shows, mapped to the platform's native cursor.
/// </summary>
internal enum SplitterCursorShape
{
    Default,
    ResizeColumns,
    ResizeRows
}

/// <summary>
/// Sets the operating-system pointer cursor for the panel splitters, for heads that must drive the native
/// cursor directly rather than through the unreliable managed cursor.
/// </summary>
internal interface ISplitterCursorController
{
    /// <summary>
    /// Sets the OS pointer cursor to the given splitter shape.
    /// </summary>
    void SetCursor(SplitterCursorShape shape);
}
