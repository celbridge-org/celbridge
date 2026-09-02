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
    /// must not act on it either.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The AppKit responder chain, because no surface answers for the verb.
    /// </summary>
    ResponderChain
}

/// <summary>
/// Routes a standard edit verb to the surface that owns it, so the Edit menu and the keyboard reach the same
/// place. A surface that answers for the verb performs it through the app's edit command, because the
/// responder chain's own cut: and selectAll: act on the native web view without telling the editor. A surface
/// that does not answer (an external page, a rich text editor that keeps the platform clipboard) leaves the
/// verb to the responder chain, whose native handling is what those surfaces need. macOS-only.
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

        // A surface whose clipboard the host mediates has already reported that it cannot perform the verb,
        // so the native handling would edit the page behind the editor's back.
        return editTarget.HostMediatedClipboard
            && intent is EditIntent.Cut or EditIntent.Copy or EditIntent.Paste
            ? EditRouting.Unavailable
            : EditRouting.ResponderChain;
    }

    /// <summary>
    /// Performs the verb on the focused surface when it answers for it. Returns whether the surface
    /// answered, in which case the responder chain must not also see the verb.
    /// </summary>
    public static bool TryPerform(EditIntent intent, IFocusService? focusService, ICommandService? commandService)
    {
        var routing = Resolve(intent, focusService);

        if (routing == EditRouting.Surface)
        {
            commandService?.Execute<IPerformEditCommand>(command => command.Intent = intent);
        }

        return routing != EditRouting.ResponderChain;
    }
}
