namespace Celbridge.Documents;

/// <summary>
/// A document view that owns a find affordance for its content. A host find action can query and drive this
/// capability without knowing the concrete view type; a view that has no find, or that delegates find to its
/// own content, does not implement it.
/// </summary>
public interface IFindableDocument
{
    /// <summary>
    /// True when the view can currently begin a find (its content is ready). Drives the enabled state of any
    /// host find affordance.
    /// </summary>
    bool CanFind { get; }

    /// <summary>
    /// Begins a find session, revealing and focusing the view's find affordance. Returns false when the view
    /// is not ready to find.
    /// </summary>
    bool TryBeginFind();
}
