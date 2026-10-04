using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Logging;
using Celbridge.WorkspaceUI.Helpers;
using Windows.ApplicationModel.DataTransfer;

namespace Celbridge.WorkspaceUI.Commands;

public class CopyTextToClipboardCommand : CommandBase, ICopyTextToClipboardCommand
{
    [RedactedInLogs]
    public string Text { get; set; } = string.Empty;
    public DataTransferMode TransferMode { get; set; }

    public override async Task<Result> ExecuteAsync()
    {
        if (string.IsNullOrEmpty(Text))
        {
            // Copying empty text to the clipboard is a no-op
            return Result.Ok();
        }

        var dataPackage = new DataPackage();
        dataPackage.SetText(Text);

        if (TransferMode == DataTransferMode.Move)
        {
            dataPackage.RequestedOperation = DataPackageOperation.Move;
        }
        else
        {
            dataPackage.RequestedOperation = DataPackageOperation.Copy; 
        }

        return await ClipboardWriter.SetContentAsync(dataPackage);
    }
}
