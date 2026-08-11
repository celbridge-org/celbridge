namespace Celbridge.UserInterface;

/// <summary>
/// The workspace chrome level, from full editor to content-only. Orthogonal to fullscreen: a layout
/// mode can be applied whether or not the window is filling the screen.
/// </summary>
public enum LayoutMode
{
    /// <summary>
    /// The full editor: side panels, the application toolbar, and document tabs are all visible.
    /// </summary>
    Default,

    /// <summary>
    /// The active document's area fills the workspace, with the Utility Panel and the other two
    /// document areas hidden. The area keeps its split, so a split one still shows both of its
    /// documents. The application toolbar and document tabs remain visible.
    /// </summary>
    Focus,

    /// <summary>
    /// As Focus, with the application toolbar and document tabs hidden as well, leaving only the
    /// document content.
    /// </summary>
    Presentation
}

/// <summary>
/// A requested change to the window layout. Layout-mode transitions and the fullscreen toggle are
/// independent: changing one does not change the other.
/// </summary>
public enum LayoutTransition
{
    /// <summary>
    /// Switch to the Default layout (all chrome visible).
    /// </summary>
    Default,

    /// <summary>
    /// Switch to the Focus layout (the active document's area alone).
    /// </summary>
    Focus,

    /// <summary>
    /// Switch to the Presentation layout (document content only).
    /// </summary>
    Presentation,

    /// <summary>
    /// Toggle between the Default and Focus layouts.
    /// </summary>
    ToggleFocus,

    /// <summary>
    /// Toggle the window's fullscreen state. Used on platforms without native window-chrome fullscreen
    /// controls. On macOS the OS provides fullscreen through the native title bar.
    /// </summary>
    ToggleFullScreen,

    /// <summary>
    /// Restore all panels to visible at their default sizes and return to the Default layout.
    /// </summary>
    ResetLayout
}
