using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Who performs a standard edit verb.
/// </summary>
internal enum EditRouting
{
    /// <summary>
    /// The focused surface, through the app's edit command.
    /// </summary>
    Surface,

    /// <summary>
    /// Nobody. The focused surface owns the verb but cannot perform it right now, so the responder chain
    /// must not act on it.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The AppKit responder chain, because no focused surface handles the verb.
    /// </summary>
    ResponderChain
}

/// <summary>
/// Routes a standard edit verb to the surface that owns it. A surface that does not handle the verb leaves
/// it to the AppKit responder chain. macOS-only.
/// </summary>
internal static class MacOSEditCommands
{
    /// <summary>
    /// Who should perform the verb given the currently focused surface.
    /// </summary>
    public static EditRouting Resolve(EditIntent intent, IFocusService? focusService)
    {
        var editTarget = focusService?.EditTarget;
        if (editTarget is null)
        {
            return EditRouting.ResponderChain;
        }

        if (editTarget.CanPerformEdit(intent))
        {
            return EditRouting.Surface;
        }

        // The host mediates this surface's clipboard, so AppKit's own cut: or paste: would change the page
        // without telling the editor.
        if (editTarget.HostMediatedClipboard
            && intent is EditIntent.Cut or EditIntent.Copy or EditIntent.Paste)
        {
            return EditRouting.Unavailable;
        }

        return EditRouting.ResponderChain;
    }

    /// <summary>
    /// Performs the verb on the focused surface when it owns it. Returns who the verb was routed to.
    /// </summary>
    public static EditRouting Perform(EditIntent intent, IFocusService? focusService, ICommandService? commandService)
    {
        var routing = Resolve(intent, focusService);

        if (routing == EditRouting.Surface)
        {
            commandService?.Execute<IPerformEditCommand>(command => command.Intent = intent);
        }

        return routing;
    }
}
