using Celbridge.UserInterface;

namespace Celbridge.Documents.Views;

/// <summary>
/// Decides what a focus report does to the active document: whether the report makes its document active,
/// whether a change of active document carries the keyboard to it, and whether a press inside a document
/// hands the keyboard to it. Pure functions, so the rules that keep focus and activation from driving
/// each other can be exercised without a live web surface or window.
/// </summary>
public static class ActiveDocumentFocusPolicy
{
    /// <summary>
    /// Whether a focus report naming a document should make that document the active one.
    /// </summary>
    public static bool ShouldActivate(ResourceKey documentResource, ResourceKey activeDocument)
    {
        // A report that names no document cannot activate one.
        if (documentResource.IsEmpty)
        {
            return false;
        }

        // Focus moving between the controls inside one document reports that document on every step.
        // Activating each time would reselect its tab and re-broadcast the active document for a move that
        // changed nothing.
        return documentResource != activeDocument;
    }

    /// <summary>
    /// Whether the document that just became active should be given keyboard focus.
    /// </summary>
    public static bool ShouldCarryFocus(ResourceKey documentResource, ActiveDocumentChangeReason reason)
    {
        // The last document closed, so there is nothing to carry the keyboard to. The surface that held it
        // reports its own teardown.
        if (documentResource.IsEmpty)
        {
            return false;
        }

        // A restore is not something the user asked for, and a document made active by its own surface
        // taking the keyboard already has it. Granting focus to the latter would let two web surfaces
        // trade it forever: each grant reports focus, each report makes its document active, and each
        // activation grants focus again. A rename leaves the keyboard where the rename was made from.
        return reason == ActiveDocumentChangeReason.Activated;
    }

    /// <summary>
    /// Whether a press inside a document should hand the keyboard to that document, judged from where focus
    /// settled after the press.
    /// </summary>
    public static bool ShouldFocusPressedDocument(bool focusIsInPressedDocument, FocusLocation focusLocation)
    {
        // The press reached a control in the document, which now holds the keyboard.
        if (focusIsInPressedDocument)
        {
            return false;
        }

        // The press opened a dialog, flyout or menu, which owns the keyboard while it is open. Taking focus
        // back to the document would leave it on screen with no keys reaching it.
        return focusLocation != FocusLocation.Popup;
    }
}
