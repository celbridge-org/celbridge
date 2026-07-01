namespace Celbridge.Workspace;

/// <summary>
/// The standard editing verbs, platform- and surface-agnostic. Every surface understands the common verbs.
/// Delete, Duplicate, and Rename are extensions offered by surfaces that support them.
/// </summary>
public enum EditIntent
{
    Copy,
    Cut,
    Paste,
    SelectAll,
    Delete,
    Duplicate,
    Rename,
    Undo,
    Redo
}

/// <summary>
/// A focusable surface that performs the standard editing verbs on its own selection. A native panel acts
/// on its domain selection (e.g. the Explorer copies resources). A WebView editor forwards the intent to
/// its own editor command.
/// </summary>
public interface IEditTarget
{
    /// <summary>
    /// Whether the surface can currently perform the intent (e.g. Copy requires a non-empty selection).
    /// </summary>
    bool CanPerformEdit(EditIntent intent);

    /// <summary>
    /// Performs the intent on the surface's current selection. May be fire-and-forget.
    /// </summary>
    void PerformEdit(EditIntent intent);
}

/// <summary>
/// Which edit verbs a WebView editor can currently perform, reported by the editor over the bridge
/// whenever its selection changes.
/// </summary>
public record EditAvailability(
    bool CanCopy,
    bool CanCut,
    bool CanPaste,
    bool CanSelectAll,
    bool CanUndo,
    bool CanRedo)
{
    /// <summary>
    /// The default for an editor that has reported nothing yet: nothing is allowed.
    /// </summary>
    public static EditAvailability None { get; } = new(false, false, false, false, false, false);

    /// <summary>
    /// Whether the given intent is one of the currently allowed verbs.
    /// </summary>
    public bool Allows(EditIntent intent)
    {
        return intent switch
        {
            EditIntent.Copy => CanCopy,
            EditIntent.Cut => CanCut,
            EditIntent.Paste => CanPaste,
            EditIntent.SelectAll => CanSelectAll,
            EditIntent.Undo => CanUndo,
            EditIntent.Redo => CanRedo,
            _ => false
        };
    }
}
