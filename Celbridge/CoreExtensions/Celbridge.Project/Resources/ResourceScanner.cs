using System.Collections.ObjectModel;

using Path = System.IO.Path;

namespace Celbridge.Project.Resources;

public static class ResourceScanner
{
    public static Result<ObservableCollection<Resource>> ScanFolderResources(string path)
    {
        var scanResult = ScanFolder(path);
        if (scanResult.IsFailure)
        {
            return Result<ObservableCollection<Resource>>.Fail(scanResult.Error);
        }

        var scannedFolder = scanResult.Value;
        return Result<ObservableCollection<Resource>>.Ok(scannedFolder.Children);
    }

    private static Result<FolderResource> ScanFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return Result<FolderResource>.Fail($"Failed to scan folder. Path not found '{path}'");
            }

            var folderResource = new FolderResource(Path.GetFileName(path));

            var sortedDirectories = Directory.GetDirectories(path).OrderBy(d => d).ToList();
            foreach (var directory in sortedDirectories)
            {
                var scanResult = ScanFolder(directory);
                if (scanResult.IsFailure)
                {
                    return scanResult;
                }
                var resource = scanResult.Value;

                folderResource.AddChild(resource);
            }

            var sortedFiles = Directory.GetFiles(path).OrderBy(f => f).ToList();
            foreach (var file in sortedFiles)
            {
                folderResource.AddChild(new FileResource(Path.GetFileName(file)));
            }

            return Result<FolderResource>.Ok(folderResource);
        }
        catch (Exception ex)
        {
            return Result<FolderResource>.Fail($"Failed to scan folder '{path}'. {ex.Message}");
        }
    }
}