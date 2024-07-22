namespace Celbridge.Dialog;

/// <summary>
/// A token representing an active progress dialog.
/// The progress dialog will be displayed as long as any token is active, and will display the title of 
/// the most recently acquired token that is still active.
/// </summary>
public interface IProgressDialogToken
{
    /// <summary>
    /// A unique identifier for this token.
    /// </summary>
    public Guid Token { get; }

    /// <summary>
    /// The title to display in the progress dialog when this token is active.
    /// </summary>
    public string DialogTitle { get; init; }
}
