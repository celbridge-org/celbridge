using Celbridge.Commands;

namespace Celbridge.DataTransfer;

/// <summary>
/// Copies text to the clipboard.
/// </summary>
public interface ICopyTextToClipboardCommand : IExecutableCommand
{
    /// <summary>
    /// Text to copy to the clipboard.
    /// </summary>
    string Text { get; set; }

    /// <summary>
    /// Specifies if the text is copied or cut to the clipboard.
    /// </summary>
    DataTransferMode TransferMode { get; set; }
}
