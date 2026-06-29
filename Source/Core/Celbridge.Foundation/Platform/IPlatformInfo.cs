namespace Celbridge.Platform;

/// <summary>
/// The keyboard modifier that issues application commands on the current platform: Control on Windows and
/// Linux, Command on macOS.
/// </summary>
public enum CommandModifierKey
{
    Control,
    Command
}

/// <summary>
/// The single oracle for platform-divergent behaviour. A layer that must behave differently on one platform
/// asks a semantic capability question here rather than checking the operating system or build head directly,
/// so the OS and head branching lives in one place.
/// </summary>
public interface IPlatformInfo
{
    /// <summary>
    /// Whether the platform supplies its own application menu bar (macOS), so the in-window hamburger menu is
    /// not mounted.
    /// </summary>
    bool UsesNativeMenuBar { get; }

    /// <summary>
    /// Whether the platform offers full-screen through its own window chrome (the macOS title-bar green
    /// button), so the app does not present its own full-screen toggle.
    /// </summary>
    bool HasNativeFullScreenAffordance { get; }

    /// <summary>
    /// Whether the head draws a custom title bar that the system caption buttons sit over, so the toolbar
    /// reserves a column for them.
    /// </summary>
    bool ReservesWindowCaptionButtons { get; }

    /// <summary>
    /// Whether the host chrome (the custom title bar) shows the open project's name, so the explorer banner
    /// shows a generic title instead of duplicating it.
    /// </summary>
    bool HostShowsProjectTitleInChrome { get; }

    /// <summary>
    /// The keyboard modifier that issues application commands on this platform.
    /// </summary>
    CommandModifierKey CommandModifier { get; }

    /// <summary>
    /// Whether the Uno Skia head needs a list row's selection visual re-asserted after selection changes
    /// because it does not repaint the highlight until a later pointer event.
    /// </summary>
    bool RequiresSkiaSelectionRepaint { get; }

    /// <summary>
    /// Whether the Uno Skia head can throw a layout exception when a tab is selected in an unmeasured
    /// overflowing strip, so the selection is retried on the next dispatcher cycle.
    /// </summary>
    bool RequiresSkiaLayoutRetry { get; }
}
