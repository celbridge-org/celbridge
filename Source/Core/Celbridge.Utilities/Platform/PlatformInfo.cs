using Celbridge.Platform;

namespace Celbridge.Utilities.Platform;

/// <summary>
/// Answers platform capability questions from runtime operating-system checks and the build head, keeping all
/// of that branching in one place so the rest of the codebase stays free of OS and head conditionals.
/// </summary>
public sealed class PlatformInfo : IPlatformInfo
{
    public bool UsesNativeMenuBar => OperatingSystem.IsMacOS();

    public bool HasNativeFullScreenAffordance => OperatingSystem.IsMacOS();

    public bool ReservesWindowCaptionButtons
    {
        get
        {
#if WINDOWS
            return true;
#else
            return false;
#endif
        }
    }

    public bool HostShowsProjectTitleInChrome
    {
        get
        {
#if WINDOWS
            return true;
#else
            return false;
#endif
        }
    }

    public CommandModifierKey CommandModifier => OperatingSystem.IsMacOS()
        ? CommandModifierKey.Command
        : CommandModifierKey.Control;

    public bool RequiresSkiaSelectionRepaint => OperatingSystem.IsMacOS();

    public bool RequiresSkiaLayoutRetry => OperatingSystem.IsMacOS();
}
