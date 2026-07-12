namespace Celbridge.Dialog;

/// <summary>
/// A modal dialog that requests the user to confirm an action.
/// </summary>
public interface IConfirmationDialog
{
    /// <summary>
    /// The title text displayed at the top of the confirmation dialog.
    /// </summary>
    string TitleText { get; set; }

    /// <summary>
    /// The message text displayed in the body of the confirmation dialog.
    /// </summary>
    string MessageText { get; set; }

    /// <summary>
    /// The text for the primary (confirm) button. Defaults to localized "OK" text.
    /// </summary>
    string PrimaryButtonText { get; set; }

    /// <summary>
    /// The text for the secondary (cancel) button. Defaults to localized "Cancel" text.
    /// </summary>
    string SecondaryButtonText { get; set; }

    /// <summary>
    /// When true, the confirm button is styled as a destructive action and keyboard focus starts on
    /// the cancel button, so pressing Enter cancels rather than carrying out the action.
    /// </summary>
    bool IsDestructive { get; set; }

    /// <summary>
    /// Present the confirms dialog to the user.
    /// The async call completes when the user closes the dialog by accepting or cancelling the action.
    /// </summary>
    Task<bool> ShowDialogAsync();
}
