namespace Celbridge.Dialog;

/// <summary>
/// Broadcast by IDialogService to answer the currently-displayed modal dialog
/// on behalf of an automated test. The payload is interpreted by whichever
/// dialog is listening: an empty payload answers a confirmation dialog
/// affirmatively; a non-empty string is the text for an input-text dialog.
/// A dialog that receives a payload incompatible with its own contract logs
/// a warning and continues to block on the user.
/// </summary>
public record DialogAnswerMessage(string Payload);
