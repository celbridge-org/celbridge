namespace Celbridge.Dialog;

/// <summary>
/// A token representing an active progress dialog.
/// The progress dialog will be displayed as long as any token is active, and will display the title of 
/// the most recently acquired token that is still active.
/// </summary>
public interface IProgressDialogToken : IDisposable
{
    /// <summary>
    /// The title to display in the progress dialog when this token is active.
    /// </summary>
    string DialogTitle { get; }
}
