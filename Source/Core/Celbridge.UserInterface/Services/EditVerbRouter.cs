using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

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
    /// The text control holding managed keyboard focus.
    /// </summary>
    TextControl,

    /// <summary>
    /// Nobody. The owner of the verb cannot perform it right now, so the platform must not act on it.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The platform, because nothing focused handles the verb. macOS offers it to the AppKit responder
    /// chain; the other heads have nowhere further to send it.
    /// </summary>
    ResponderChain
}

/// <summary>
/// Routes a standard edit verb to the surface that owns it, for every caller that offers one: the menus and
/// the macOS Command chords. A verb nothing focused owns is left to the platform.
/// </summary>
internal static class EditVerbRouter
{
    /// <summary>
    /// Who should perform the verb given the currently focused surface and text control, and whether a modal
    /// dialog holds the keyboard.
    /// </summary>
    public static EditRouting Resolve(
        EditIntent intent,
        IFocusService? focusService,
        ITextControlEditing? textControlEditing,
        bool isDialogOpen)
    {
        // A dialog owns the keyboard while it is up, so the verb belongs to the control inside it rather
        // than to the panel behind it, whose edit target the focus service still holds.
        if (isDialogOpen)
        {
            return ResolveTextControl(intent, textControlEditing);
        }

        var editTarget = focusService?.EditTarget;
        if (editTarget is null)
        {
            return ResolveTextControl(intent, textControlEditing);
        }

        if (editTarget.CanPerformEdit(intent))
        {
            return EditRouting.Surface;
        }

        // A focused text control answers for the verbs the panel's own edit target does not, so a field in
        // the chrome of a panel can still be edited.
        var textControlRouting = ResolveTextControl(intent, textControlEditing);
        if (textControlRouting != EditRouting.ResponderChain)
        {
            return textControlRouting;
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
    /// Performs the verb on whichever of the focused surface and the focused text control owns it. Returns
    /// who the verb was routed to.
    /// </summary>
    public static EditRouting Perform(
        EditIntent intent,
        IFocusService? focusService,
        ITextControlEditing? textControlEditing,
        ICommandService? commandService,
        bool isDialogOpen)
    {
        var routing = Resolve(intent, focusService, textControlEditing, isDialogOpen);

        switch (routing)
        {
            case EditRouting.Surface:
                commandService?.Execute<IPerformEditCommand>(command => command.Intent = intent);
                break;

            case EditRouting.TextControl:
                textControlEditing?.TryPerformEdit(intent);
                break;
        }

        return routing;
    }

    // A text control holding the keyboard owns every standard verb, so one it cannot perform right now is
    // unavailable, and the platform must not be offered it.
    private static EditRouting ResolveTextControl(EditIntent intent, ITextControlEditing? textControlEditing)
    {
        if (textControlEditing?.IsTextControlFocused != true)
        {
            return EditRouting.ResponderChain;
        }

        return textControlEditing.CanPerformEdit(intent)
            ? EditRouting.TextControl
            : EditRouting.Unavailable;
    }
}
