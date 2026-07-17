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

    public bool PickersRequireWindowHandle
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

    public string FileManagerNameStringKey => OperatingSystem.IsMacOS()
        ? "Platform_FileManager_Finder"
        : "Platform_FileManager_FileExplorer";

    public bool TreatsBackspaceAsDeleteKey => OperatingSystem.IsMacOS();

    public bool TreatsCtrlYAsRedo => OperatingSystem.IsWindows();

    public bool SuppressListItemTransitions
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

    public bool RequiresMacOSSelectionRepaint => OperatingSystem.IsMacOS();

    public bool RequiresMacOSLayoutRetry => OperatingSystem.IsMacOS();

    public bool RequiresMacOSTabScrollIntoView => OperatingSystem.IsMacOS();

    public bool UsesPointerDrivenTabDrag
    {
        get
        {
#if WINDOWS
            return false;
#else
            return true;
#endif
        }
    }
}
