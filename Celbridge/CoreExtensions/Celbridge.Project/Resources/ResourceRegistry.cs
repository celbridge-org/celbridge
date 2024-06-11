using CommunityToolkit.Diagnostics;
using System.Collections.ObjectModel;

using Path = System.IO.Path;

namespace Celbridge.Project.Resources;

public class ResourceRegistry
{
    private readonly string _projectFolder;
    private FolderResource? _rootFolder;

    public ObservableCollection<Resource> Resources
    {
        get
        {
            Guard.IsNotNull(_rootFolder);
            return _rootFolder.Children;
        }
    }

    public ResourceRegistry(string projectFolder)
    {
        _projectFolder = projectFolder;
    }

    public Result ScanResources()
    {
        var scanResult = ScanFolder(_projectFolder);
        if (scanResult.IsFailure)
        {
            return Result.Fail(scanResult.Error);
        }

        if (_rootFolder is null)
        {
            // First time scanning the root folder
            _rootFolder = scanResult.Value;
        }
        else
        {
            // Update the existing root folder
            _rootFolder.Children.Clear();
            foreach (var child in scanResult.Value.Children)
            {
                _rootFolder.AddChild(child);
            }
        }

        return Result.Ok();
    }

    private Result<FolderResource> ScanFolder(string path)
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