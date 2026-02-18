using Celbridge.Commands;
using Celbridge.DataTransfer;
using Windows.ApplicationModel.DataTransfer;

namespace Celbridge.WorkspaceUI.Commands;

public class CopyTextToClipboardCommand : CommandBase, ICopyTextToClipboardCommand
{
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

        Clipboard.SetContent(dataPackage);

        await Task.CompletedTask;
        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void CopyTextToClipboard(string text)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICopyTextToClipboardCommand>(command =>
        {
            command.Text = text;
        });
    }
}
