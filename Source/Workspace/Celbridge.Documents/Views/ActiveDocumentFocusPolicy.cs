namespace Celbridge.Documents.Views;

/// <summary>
/// Decides whether a change of active document carries the keyboard to that document. A pure function
/// because the rule keeps focus and activation from driving each other, which a live web surface would
/// otherwise be needed to exercise.
/// </summary>
public static class ActiveDocumentFocusPolicy
{
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
