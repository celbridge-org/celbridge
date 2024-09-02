using Celbridge.Explorer.Models;
using Celbridge.UserInterface;
using System.Text;

namespace Celbridge.Explorer.Services
{
    public class ResourceRegistry : IResourceRegistry
    {
        private readonly IMessengerService _messengerService;
        private readonly IIconService _iconService;

        public string ProjectFolderPath { get; set; } = string.Empty;

        private FolderResource _rootFolder = new FolderResource(string.Empty, null);

        public IFolderResource RootFolder => _rootFolder;

        public ResourceRegistry(
            IMessengerService messengerService,
            IIconService iconService)
        {
            _messengerService = messengerService;
            _iconService = iconService;
        }

        public ResourceKey GetResourceKey(IResource resource)
        {
            try
            {
                var sb = new StringBuilder();
                void AddResourceKeySegment(IResource resource)
                {
                    if (resource.ParentFolder is null)
                    {
                        return;
                    }

                    // Build path by recursively visiting each parent folders
                    AddResourceKeySegment(resource.ParentFolder);

                    // The trick is to append the path segment after we've visited the parent.
                    // This ensures the path segments are appended in the right order.
                    if (sb.Length > 0)
                    {
                        sb.Append("/");
                    }
                    sb.Append(resource.Name);
                }
                AddResourceKeySegment(resource);

                var resourceKey = new ResourceKey(sb.ToString());

                return resourceKey;
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Failed to get resource key for '{resource}'", ex);
            }
        }

        public Result<ResourceKey> GetResourceKey(string resourcePath)
        {
            try
            {
                var normalizedPath = Path.GetFullPath(resourcePath);
                var normalizedProjectPath = Path.GetFullPath(ProjectFolderPath);

                if (!normalizedPath.StartsWith(normalizedProjectPath))
                {
                    return Result<ResourceKey>.Fail($"The path '{resourcePath}' is not in the project folder '{ProjectFolderPath}'.");
                }

                var resourceKey = normalizedPath.Substring(ProjectFolderPath.Length)
                    .Replace('\\', '/')
                    .Trim('/');

                return Result<ResourceKey>.Ok(resourceKey);
            }
            catch (Exception ex)
            {
                return Result<ResourceKey>.Fail($"Failed to get resource key for '{resourcePath}'. {ex.Message}");
            }
        }

        public string GetResourcePath(IResource resource)
        {
            try
            {
                var resourceKey = GetResourceKey(resource);
                var path = GetResourcePath(resourceKey);
                return path;
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Failed to get path for resource '{resource}'", ex);
            }
        }

        public string GetResourcePath(ResourceKey resource)
        {
            try
            {
                var resourcePath = Path.Combine(ProjectFolderPath, resource);
                var normalized = Path.GetFullPath(resourcePath);

                return normalized;
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Failed to get path for resource '{resource}'.", ex);
            }
        }

        public Result<IResource> GetResource(ResourceKey resource)
        {
            if (resource.IsEmpty)
            {
                // An empty resource key refers to the root folder
                return Result<IResource>.Ok(_rootFolder);
            }

            var segments = resource.ToString().Split('/');
            var searchFolder = _rootFolder;

            // Attempt to match each segment with the corresponding resource in the tree
            var segmentIndex = 0;
            while (segmentIndex < segments.Length)
            {
                FolderResource? matchingFolder = null;
                string segment = segments[segmentIndex];
                foreach (var childResource in searchFolder.Children)
                {
                    if (childResource is FolderResource childFolder &&
                        childFolder.Name == segment)
                    {
                        if (segmentIndex == segments.Length - 1)
                        {
                            // The folder name matches the last segment in the key, so this is the
                            // folder resource we're looking for.
                            return Result<IResource>.Ok(childFolder);
                        }

                        // This folder resource matches this segment in the key, so we can move onto
                        // searching for the next segment.
                        matchingFolder = childFolder;
                        break;
                    }
                    else if (childResource is FileResource childFile &&
                             childFile.Name == segment &&
                             segmentIndex == segments.Length - 1)
                    {
                        // The file name matches the last segment in the key, so this is the
                        // file resource we're looking for.
                        return Result<IResource>.Ok(childFile);
                    }
                }

                if (matchingFolder is null)
                {
                    break;
                }

                searchFolder = matchingFolder;
                segmentIndex++;
            }

            return Result<IResource>.Fail($"Failed to find a resource matching the resource key '{resource}'.");
        }

        public ResourceKey ResolveDestinationResource(ResourceKey sourceResource, ResourceKey destResource)
        {
            string output = destResource;

            var getResult = GetResource(destResource);
            if (getResult.IsSuccess)
            {
                var resource = getResult.Value;
                if (resource is IFolderResource)
                {
                    if (destResource.IsEmpty)
                    {
                        // Destination is the root folder
                        output = sourceResource.ResourceName;
                    }
                    else
                    {
                        if (sourceResource == destResource)
                        {
                            // Source and destination are the same folder. This case is allowed because
                            // the user may duplicate a folder by copying and pasting it to the same destination.
                            output = destResource;
                        }
                        else
                        {
                            // Destination is a folder, so append the source resource name to this folder.
                            output = destResource.Combine(sourceResource.ResourceName);
                        }
                    }
                }
            }

            return output;
        }

        public ResourceKey ResolveSourcePathDestinationResource(string sourcePath, ResourceKey destResource)
        {
            string output = destResource;

            var getResult = GetResource(destResource);
            if (getResult.IsSuccess)
            {
                var resource = getResult.Value;
                if (resource is IFolderResource)
                {
                    var filename = Path.GetFileName(sourcePath);
                    if (destResource.IsEmpty)
                    {
                        // Destination is the root folder
                        output = filename;
                    }
                    else
                    {
                        // Destination is a folder, so append the source filename to this folder.
                        output = destResource.Combine(filename);
                    }
                }
            }

            return output;
        }


        public ResourceKey GetContextMenuItemFolder(IResource? resource)
        {
            IFolderResource? destFolder = null;
            switch (resource)
            {
                case IFolderResource folder:
                    destFolder = folder;
                    break;
                case IFileResource file:
                    destFolder = file.ParentFolder;
                    break;
            }
            if (destFolder is null)
            {
                destFolder = _rootFolder;
            }

            return GetResourceKey(destFolder);
        }

        public Result UpdateResourceRegistry()
        {
            try
            {
                SynchronizeFolder(_rootFolder, ProjectFolderPath);

                ExpandedFolders.Remove(string.Empty);
                ExpandedFolders.Remove((expandedFolder) => GetResource(expandedFolder).IsFailure);

                _messengerService.Send(new ResourceRegistryUpdatedMessage());

                return Result.Ok();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to update resource registry. {ex.Message}");
            }
        }

        private void SynchronizeFolder(FolderResource folderResource, string folderPath)
        {
            // Apply expanded folder state

            var folderResourceKey = GetResourceKey(folderResource);
            folderResource.IsExpanded = IsFolderExpanded(folderResourceKey);

            // Update child resources

            var existingChildren = folderResource.Children.ToDictionary(child => child.Name);

            var subFolderPaths = Directory.GetDirectories(folderPath).OrderBy(d => d).ToList();
            var filePaths = Directory.GetFiles(folderPath).OrderBy(f => f).ToList();

            folderResource.Children.Clear();

            foreach (var subFolderPath in subFolderPaths)
            {
                var folderName = Path.GetFileName(subFolderPath);
                if (existingChildren.TryGetValue(folderName, out var existingChild) && existingChild is FolderResource existingFolder)
                {
                    SynchronizeFolder(existingFolder, subFolderPath);
                    folderResource.AddChild(existingFolder);
                }
                else
                {
                    var newFolder = new FolderResource(folderName, folderResource);
                    SynchronizeFolder(newFolder, subFolderPath);
                    folderResource.AddChild(newFolder);
                }
            }

            foreach (var filePath in filePaths)
            {
                var fileName = Path.GetFileName(filePath);
                if (existingChildren.TryGetValue(fileName, out var existingChild) && existingChild is FileResource)
                {
                    folderResource.AddChild(existingChild);
                }
                else
                {
                    // Lookup the icon for this type of file 
                    IconDefinition iconDefinition;
                    var fileExtension = Path.GetExtension(filePath).TrimStart('.');

                    var getIconResult = _iconService.GetIconForFileExtension(fileExtension);
                    if (getIconResult.IsSuccess)
                    {
                        iconDefinition = getIconResult.Value;
                    }
                    else
                    {
                        iconDefinition = _iconService.DefaultFileIcon;
                    }

                    var fileResource = new FileResource(fileName, folderResource, iconDefinition);

                    folderResource.AddChild(fileResource);
                }
            }

            folderResource.Children = folderResource.Children
                .OrderBy(child => child is IFolderResource ? 0 : 1)
                .ThenBy(child => child.Name)
                .ToList();
        }

        public List<string> ExpandedFolders { get; } = new();

        public void SetFolderIsExpanded(ResourceKey folderResource, bool isExpanded)
        {
            if (isExpanded)
            {
                if (!folderResource.IsEmpty &&
                    !ExpandedFolders.Contains(folderResource))
                {
                    ExpandedFolders.Add(folderResource);
                    ExpandedFolders.Sort();
                }
            }
            else
            {
                ExpandedFolders.Remove(folderResource);
            }
        }

        public bool IsFolderExpanded(ResourceKey folderResource)
        {
            return ExpandedFolders.Contains(folderResource);
        }
    }
}
