namespace Celbridge.Documents.Views;

/// <summary>
/// Decides what a focus report does to the active document: whether the report makes its document active,
/// and whether a change of active document carries the keyboard to it. Pure functions because the rules keep
/// focus and activation from driving each other, which a live web surface would otherwise be needed to
/// exercise.
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
        // taking the keyboard already has it. Granting focus to the latter is what lets two web surfaces
        // trade it without settling: each grant reports focus, each report makes its document active, and
        // each activation grants focus again.
        return reason == ActiveDocumentChangeReason.Activated;
    }
}
