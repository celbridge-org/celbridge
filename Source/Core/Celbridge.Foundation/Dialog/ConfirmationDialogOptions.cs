namespace Celbridge.Dialog;

/// <summary>
/// Optional presentation settings for a confirmation dialog.
/// </summary>
public record ConfirmationDialogOptions
{
    /// <summary>
    /// Overrides the primary (confirm) button label. Null uses the localized "OK" default.
    /// </summary>
    public string? PrimaryButtonText { get; init; }

    /// <summary>
    /// Overrides the secondary (cancel) button label. Null uses the localized "Cancel" default.
    /// </summary>
    public string? SecondaryButtonText { get; init; }

    /// <summary>
    /// When true, the confirm button is styled as a destructive action and keyboard focus starts on
    /// the cancel button, so pressing Enter cancels rather than carrying out the action. Defaults to false.
    /// </summary>
    public bool IsDestructive { get; init; }
}
