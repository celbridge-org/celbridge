using Celbridge.DataTransfer;
using Windows.ApplicationModel.DataTransfer;

namespace Celbridge.WorkspaceUI.Platform;

/// <summary>
/// File clipboard backed by the WinRT data-transfer clipboard (storage items). Used on Windows, where
/// the storage-item clipboard round-trips natively.
/// </summary>
public sealed class WinRtFileClipboard : IFileClipboard
{
    public async Task<Result> SetFilesAsync(IReadOnlyList<ClipboardFile> files, DataTransferMode transferMode)
    {
        var storageItems = new List<IStorageItem>();
        foreach (var file in files)
        {
            if (file.IsFolder)
            {
                var storageFolder = await StorageFolder.GetFolderFromPathAsync(file.Path);
                if (storageFolder is not null)
                {
                    storageItems.Add(storageFolder);
                }
            }
            else
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(file.Path);
                if (storageFile is not null)
                {
                    storageItems.Add(storageFile);
                }
            }
        }

        if (storageItems.Count == 0)
        {
            return Result.Ok();
        }

        var dataPackage = new DataPackage
        {
            RequestedOperation = transferMode == DataTransferMode.Move
                ? DataPackageOperation.Move
                : DataPackageOperation.Copy
        };
        dataPackage.SetStorageItems(storageItems);
        Clipboard.SetContent(dataPackage);
        Clipboard.Flush();

        return Result.Ok();
    }

    public DataTransferMode? GetFileTransferMode()
    {
        var dataPackageView = Clipboard.GetContent();
        if (!dataPackageView.Contains(StandardDataFormats.StorageItems))
        {
            return null;
        }

        return MapTransferMode(dataPackageView.RequestedOperation);
    }

    public async Task<ClipboardFileContents?> GetFilesAsync()
    {
        var dataPackageView = Clipboard.GetContent();
        if (!dataPackageView.Contains(StandardDataFormats.StorageItems))
        {
            return null;
        }

        var transferMode = MapTransferMode(dataPackageView.RequestedOperation);

        var storageItems = await dataPackageView.GetStorageItemsAsync();
        var paths = new List<string>();
        foreach (var storageItem in storageItems)
        {
            // Skip storage items with empty paths (can happen with virtualized items).
            if (string.IsNullOrEmpty(storageItem.Path))
            {
                continue;
            }
            paths.Add(Path.GetFullPath(storageItem.Path));
        }

        if (paths.Count == 0)
        {
            return null;
        }

        return new ClipboardFileContents(paths, transferMode);
    }

    private static DataTransferMode MapTransferMode(DataPackageOperation operation)
    {
        return operation == DataPackageOperation.Move
            ? DataTransferMode.Move
            : DataTransferMode.Copy;
    }
}
