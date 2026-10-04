namespace Celbridge.Dialog;

/// <summary>
/// Broadcast by IDialogService to deliver a scheduled automated answer to the
/// open modal dialog of the named kind. Kind identifies the target dialog;
/// Payload carries the answer data for that dialog. Used only by the debug-only
/// dialog test automation.
/// </summary>
public record DialogAnswerMessage(DialogKind Kind, string Payload);
