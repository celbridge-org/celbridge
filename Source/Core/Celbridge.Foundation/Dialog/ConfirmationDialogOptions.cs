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
    /// When true, the cancel button becomes the accented default and takes initial focus, so pressing
    /// Enter cancels. Use for actions that cannot be undone. Defaults to false.
    /// </summary>
    public bool IsDestructive { get; init; }
}
