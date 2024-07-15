using Celbridge.BaseLibrary.Resources;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Celbridge.Project.ViewModels;

/// <summary>
/// Clipboard operation support for the resource tree view model.
/// </summary>
public partial class ResourceTreeViewModel
{
    public void CutResource(IResource resource)
    {
    }

    public void CopyResource(IResource resource)
    {
        async Task CopyResourceToClipboard(IResource resource)
        {
            try
            {
                var storageItems = new List<IStorageItem>();

                if (resource is IFileResource fileResource)
                {
                    var filePath = _projectService.ResourceRegistry.GetPath(fileResource);
                    if (string.IsNullOrEmpty(filePath))
                    {
                        return;
                    }

                    var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
                    if (storageFile != null)
                    {
                        storageItems.Add(storageFile);
                    }
                }
                else if (resource is IFolderResource folderResource)
                {
                    var folderPath = _projectService.ResourceRegistry.GetPath(folderResource);
                    if (string.IsNullOrEmpty(folderPath))
                    {
                        return;
                    }

                    var storageFolder = await StorageFolder.GetFolderFromPathAsync(folderPath);
                    if (storageFolder != null)
                    {
                        storageItems.Add(storageFolder);
                    }
                }

                if (storageItems.Count == 0)
                {
                    return;
                }

                var dataPackage = new DataPackage();
                dataPackage.RequestedOperation = DataPackageOperation.Copy;

                dataPackage.SetStorageItems(storageItems);
                Clipboard.SetContent(dataPackage);
                Clipboard.Flush();
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Failed to copy resource '{resource}' to clipboard. {ex}");
            }
        }

        _ = CopyResourceToClipboard(resource);
    }

    public void PasteResource(IResource resource)
    {
        //async Task PasteFile(string folderPath)
        //{
        //    DataPackageView dataPackageView = Clipboard.GetContent();
        //    if (dataPackageView.Contains(StandardDataFormats.StorageItems))
        //    {
        //        IReadOnlyList<IStorageItem> storageItems = await dataPackageView.GetStorageItemsAsync();

        //        if (storageItems.Count > 0)
        //        {
        //            var storageFile = storageItems[0] as StorageFile;
        //            if (storageFile != null)
        //            {
        //                // Save the file to the parent folder
        //                var sourcePath = storageFile.Path;
        //                //File.Copy(sourcePath, folderPath);
        //            }
        //        }
        //    }
        //}

        //IFolderResource parentFolder;
        //if (resource is IFileResource fileResource)
        //{
        //    parentFolder = fileResource.ParentFolder!;
        //}
        //else if (resource is IFolderResource folderResource)
        //{
        //    parentFolder = folderResource;
        //}
        //else
        //{
        //    parentFolder = _projectService.ResourceRegistry.RootFolder;
        //}

        //var folderPath = _projectService.ResourceRegistry.GetPath(parentFolder);
        //if (string.IsNullOrEmpty(folderPath))
        //{
        //    return;
        //}

        //_ = PasteFile(folderPath);
    }
}