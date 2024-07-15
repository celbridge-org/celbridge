using Celbridge.BaseLibrary.Resources;

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
        //async Task CopyFile(string filePath)
        //{
        //    StorageFile file = await StorageFile.GetFileFromPathAsync(filePath);
        //    if (file != null)
        //    {
        //        var dataPackage = new DataPackage();
        //        dataPackage.RequestedOperation = DataPackageOperation.Copy;

        //        List<IStorageItem> items = new List<IStorageItem>();
        //        items.Add(file);
        //        dataPackage.SetStorageItems(items);

        //        Clipboard.SetContent(dataPackage);
        //        Clipboard.Flush();
        //    }
        //}

        //var path = _projectService.ResourceRegistry.GetPath(resource);
        //if (string.IsNullOrEmpty(path))
        //{
        //    return;
        //}

        //if (resource is IFileResource)
        //{
        //    _ = CopyFile(path);
        //}
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