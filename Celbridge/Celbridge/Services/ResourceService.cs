using Celbridge.Models;
using Celbridge.Utils;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Uno.Extensions;

namespace Celbridge.Services
{
    public class RegistryUpdateSummary
    {
        public Dictionary<Guid, string> Visited { get; } = new(); // Resource Id => Resource Key
        public List<Guid> Added { get; } = new ();
        public List<Guid> Changed { get; } = new();
        public List<Guid> Deleted { get; } = new();

        public bool WasRegistryModified => Added.Count > 0 || Changed.Count > 0 || Deleted.Count > 0;

        public void AddSummaryEvent(string key, ResourceStatus resourceStatus)
        {
            var guid = resourceStatus.ResourceId;

            // A resource should only end up in one state per registry update
            Guard.IsFalse(Visited.ContainsKey(guid));

            Visited.Add(guid, key);

            // The deleted list is populated lated by comparing with the previously generated summary
            switch (resourceStatus.State)
            {
                case ResourceState.Static:
                    break;
                case ResourceState.Added:
                    Added.Add(guid);
                    break;
                case ResourceState.Changed:
                    Changed.Add(guid);
                    break;
            }
        }
    }

    public interface IResourceService
    {
        public Result CreateResource(Type resourceType, string resourceName);
        public Result DeleteResource(IProject project, Resource resource);
        void WriteResourceRegistryToLog(ResourceRegistry registry);
        Task<Result<RegistryUpdateSummary>> UpdateProjectResources(IProject project);
        Result<string> GetPathForNewResource(IProject project, string resourceName);
        Result<string> GetResourcePath(IProject project, Resource resource);
        Result<IEntity> FindResourceEntity(IProject project, Guid entityId);
        List<T> FindResourcesOfType<T>(IProject project) where T : Resource;
    }

    // Sent when a file resource has been removed from the resource registry
    public record ResourcesChangedMessage(List<Guid> Added, List<Guid> Changed, List<Guid> Deleted);

    public class ResourceService : IResourceService
    {
        private readonly SHA256 _hashAlgorithm = SHA256.Create();
        private readonly IMessenger _messengerService;
        private readonly IResourceTypeService _resourceTypeService;
        private IInspectorService? _inspectorService;

        public ResourceService(IMessenger messengerService, IResourceTypeService resourceTypeService)
        {
            _messengerService = messengerService;
            _resourceTypeService = resourceTypeService;
        }

        private IInspectorService InspectorService
        {
            get
            {
                _inspectorService ??= (Application.Current as App)!.Host!.Services.GetService<IInspectorService>();
                Guard.IsNotNull(_inspectorService);
                return _inspectorService;
            }
        }

        public Result CreateResource(Type resourceType, string path)
        {
            string newResourcePath;
            if (resourceType == typeof(FolderResource))
            {
                newResourcePath = path;
            }
            else
            {
                var typeResult = _resourceTypeService.GetResourceTypeInfo(resourceType);
                if (typeResult.Failure)
                {
                    return new ErrorResult($"Failed to get ResourceTypeInfo for type '{resourceType}'");
                }

                var typeInfo = typeResult.Data!;
                Guard.IsTrue(typeInfo.Extensions.Any());

                // Todo: Accept any extension that the user defines that's in the list of supported extensions
                // Todo: If the extension is not specified, then use the default.
                var extension = typeInfo.Extensions[0];
                newResourcePath = Path.ChangeExtension(path, extension);
            }

            if (Directory.Exists(newResourcePath))
            {
                return new ErrorResult($"A file or folder already exists at '{newResourcePath}'");
            }

            if (!FileUtils.IsAbsolutePathValid(newResourcePath))
            {
                return new ErrorResult($"Invalid resource path '{newResourcePath}'.");
            }

            var factoryResult = _resourceTypeService.GetFactoryMethod(resourceType);
            if (factoryResult.Failure)
            {
                var error = factoryResult as ErrorResult<Func<string, Result>>;
                return new ErrorResult(error!.Message);
            }
            var factoryMethod = factoryResult.Data!;

            var result = factoryMethod.Invoke(newResourcePath);
            if (result == null)
            {
                throw new InvalidOperationException($"Failed to create resource at '{newResourcePath}'. The Resource type {resourceType} does not implement a static CreateResource method.");
            }

            return result;
        }

        public Result DeleteResource(IProject project, Resource resource)
        {
            Guard.IsNotNull(project);
            Guard.IsNotNull(resource);

            var key = resource.GetKey();
            var projectFolder = project.ProjectFolder;
            var combined = Path.Combine(projectFolder, key);
            var path = Path.GetFullPath(combined);

            if (resource is FileResource)
            {
                var deletePathResult = FileUtils.DeletePath(path);
                if (deletePathResult.Failure)
                {
                    return deletePathResult;
                }
            }
            else if (resource is FolderResource folderResource)
            {
                foreach (var childFolderResource in folderResource.Folders)
                {
                    var deleteFolderResult = DeleteResource(project, childFolderResource);
                    if (deleteFolderResult.Failure)
                    {
                        var error = deleteFolderResult as ErrorResult<string>;
                        return new ErrorResult(error!.Message);
                    }
                }

                foreach (var childFileResource in folderResource.Files)
                {
                    var deleteFileResult = DeleteResource(project, childFileResource);
                    if (deleteFileResult.Failure)
                    {
                        var error = deleteFileResult as ErrorResult<string>;
                        return new ErrorResult(error!.Message);
                    }
                }

                var deletePathResult = FileUtils.DeletePath(path);
                if (deletePathResult.Failure)
                {
                    return deletePathResult;
                }
            }

            return new SuccessResult();
        }

        private async Task<Result<RegistryUpdateSummary>> UpdateRegistryFromFolder(ResourceRegistry registry, string projectFolder)
        {
            // Build a list of all resources currently in the resource registry
            var originalResources = GetFolderResourceMap(registry.Root);

            // Update the resource registry to match the backing resources on disk
            var summary = new RegistryUpdateSummary();
            var keyPath = string.Empty;
            var projectDir = new DirectoryInfo(projectFolder);
            await ProcessFolder(keyPath, projectDir, registry, summary);

            // Delete any resources that were originally in the resource registry but are no longer backed by a resource
            // Todo: Preserve resources when renaming / moving
            foreach (var kv in originalResources)
            {
                var resourceKey = kv.Key;
                var resourceId = kv.Value.Id;

                if (summary.Visited.ContainsKey(resourceId))
                {
                    continue;
                }

                var result = RemoveResourceFromRegistry(registry, resourceKey);
                if (result.Success)
                {
                    summary.Deleted.Add(resourceId);
                }

                // It's ok if we tried to delete a key that didn't exist - we're still in a consistent state
            }

            return new SuccessResult<RegistryUpdateSummary>(summary);
        }

        private async Task ProcessFolder(string keyPath, 
            DirectoryInfo folder, 
            ResourceRegistry registry, 
            RegistryUpdateSummary summary)
        {
            // Depth-first search
            foreach (DirectoryInfo subfolder in folder.GetDirectories().OrderBy(d => d.Name))
            {
                if ((subfolder.Attributes & System.IO.FileAttributes.ReparsePoint) != 0)
                {
                    // Skip symbolic links/junctions
                    continue;
                }

                if (subfolder.Name.StartsWith('_'))
                {
                    // Ignore folders that start with a _
                    continue;
                }

                string key;
                if (string.IsNullOrEmpty(keyPath))
                {
                    key = $"{subfolder.Name}";
                }
                else
                {
                    key = $"{keyPath}/{subfolder.Name}";
                }

                if (key == "Library" || key.StartsWith("Library/"))
                {
                    // Ignore the Library folder
                    continue;
                }

                var result = UpdateFolderResource(registry, key);
                if (result.Failure)
                {
                    var error = result as ErrorResult<ResourceStatus>;
                    Log.Error(error!.Message);
                    return;
                }

                var resourceStatus = result.Data!;
                summary.AddSummaryEvent(key, resourceStatus);

                await ProcessFolder(key, subfolder, registry, summary);
            }

            foreach (FileInfo fileInfo in folder.GetFiles().OrderBy(f => f.Name))
            {
                if (fileInfo.Extension.Equals(".celbridge", StringComparison.InvariantCultureIgnoreCase))
                {
                    // Ignore the project file
                    continue;
                }

                if (fileInfo.Name.StartsWith('_'))
                {
                    // Ignore folders that start with a _
                    continue;
                }

                var extension = Path.GetExtension(fileInfo.Name);
                var result = _resourceTypeService.GetResourceTypeForExtension(extension);
                if (result.Failure)
                {
                    // Ignore unsupported files
                    continue;
                }

                string key;
                if (string.IsNullOrEmpty(keyPath))
                {
                    key = $"{fileInfo.Name}";
                }
                else
                {
                    key = $"{keyPath}/{fileInfo.Name}";
                }

                string hash = await GetFileHash(fileInfo);

                var updateResult = UpdateFileResource(registry, key, hash);
                if (updateResult.Failure)
                {
                    var error = updateResult as ErrorResult<ResourceStatus>;
                    Log.Error(error!.Message);
                    return;
                }

                var resourceState = updateResult.Data!;
                summary.AddSummaryEvent(key, resourceState);
            }
        }

        private async Task<string> GetFileHash(FileInfo fileInfo)
        {
            // Todo: Cache hash values for fast lookup, if key, length and timestamp all match previous
            using FileStream stream = fileInfo.OpenRead();
            byte[] hashBytes = await _hashAlgorithm.ComputeHashAsync(stream);
            var hashString = BitConverter.ToString(hashBytes).Replace("-", "");
            return hashString;
        }

        private static Result<ResourceStatus> UpdateFolderResource(ResourceRegistry registry, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return new ErrorResult<ResourceStatus>("Failed to update resource becaus key is empty.");
            }

            var segments = key.Split('/');

            var searchFolder = registry.Root;
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (i == segments.Length - 1)
                {
                    var folderName = segment;

                    var searchResult = FindFolderResource(searchFolder, folderName);
                    if (searchResult.Success)
                    {
                        // Folder already exists, we're done
                        var resource = searchResult.Data!;
                        var status = new ResourceStatus(resource.Id, ResourceState.Static);
                        return new SuccessResult<ResourceStatus>(status);
                    }

                    // Add a new empty folder resource
                    var folderResource = new FolderResource()
                    {
                        Name = folderName,
                        Id = Guid.NewGuid(),
                        Description = string.Empty
                    };

                    // Todo: Insert folder in correct sorted position
                    searchFolder.Children.Add(folderResource);

                    var resourceStatus = new ResourceStatus(folderResource.Id, ResourceState.Added);
                    return new SuccessResult<ResourceStatus>(resourceStatus);
                }
                else
                {
                    var searchResult = FindFolderResource(searchFolder, segment);
                    if (searchResult.Failure)
                    {
                        // Parent folders are always added first, so if we can't find a parent folder
                        // then something has gone very wrong.
                        return new ErrorResult<ResourceStatus>($"Folder '{segment}' not found for resource key '{key}'");
                    }
                    searchFolder = searchResult.Data!;
                }
            }

            return new ErrorResult<ResourceStatus>($"Failed to add folder resource at {key}");
        }

        private Result<ResourceStatus> UpdateFileResource(ResourceRegistry registry, string key, string hash)
        {
            if (string.IsNullOrEmpty(key))
            {
                return new ErrorResult<ResourceStatus>("Failed to add resource because key is invalid.");
            }

            var segments = key.Split('/');

            var searchFolder = registry.Root;
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (i == segments.Length - 1)
                {
                    var filename = segment;
                    var extension = Path.GetExtension(filename);
                    if (extension.IsNullOrEmpty())
                    {
                        return new ErrorResult<ResourceStatus>($"Failed to add file resource '{filename}' because it has no file extension.");
                    }

                    // Todo: Handle case where the type used to valid, but isn't now
                    var result = _resourceTypeService.GetResourceTypeForExtension(extension);
                    if (result.Failure)
                    {
                        return new ErrorResult<ResourceStatus>($"Failed to add file resource '{filename}' because file extension is not supported.");
                    }
                    var entityType = result.Data!;

                    var searchResult = FindFileResource(searchFolder, filename);
                    if (searchResult.Success)
                    {
                        // The file resource already exists in the registry, so now we check the hash to see
                        // if it's been modified since the last time we checked.
                        // File resource hashes are NOT persisted between sessions so we're only detecting changes
                        // that occur while the application is running.

                        var existingFile = searchResult.Data!;
                        var isHashInitialized = !string.IsNullOrEmpty(existingFile.Hash);

                        if (isHashInitialized)
                        {
                            if (existingFile.Hash != hash)
                            {
                                // File resource has changed since the last time we processed it.
                                existingFile.Hash = hash;
                                var changedStatus = new ResourceStatus(existingFile.Id, ResourceState.Changed);
                                return new SuccessResult<ResourceStatus>(changedStatus);
                            }
                        }
                        else
                        {
                            // This file resource is already in the registry, but the hash hasn't been initialized yet, so
                            // we're currently loading the project. Populate the hash and report the file resource as unchanged.
                            existingFile.Hash = hash;
                        }

                        var staticStatus = new ResourceStatus(existingFile.Id, ResourceState.Static);
                        return new SuccessResult<ResourceStatus>(staticStatus);
                    }

                    var fileResource = Activator.CreateInstance(entityType) as FileResource;
                    Guard.IsNotNull(fileResource);

                    fileResource.Name = filename;
                    fileResource.Description = String.Empty;
                    fileResource.Id = Guid.NewGuid();
                    fileResource.Hash = hash;

                    // Todo: Insert at correct sorted position
                    searchFolder.Children.Add(fileResource);

                    var addedStatus = new ResourceStatus(fileResource.Id, ResourceState.Added);
                    return new SuccessResult<ResourceStatus>(addedStatus);
                }
                else
                {
                    var searchResult = FindFolderResource(searchFolder, segment);
                    if (searchResult.Failure)
                    {
                        // Parent folders must always be added first
                        return new ErrorResult<ResourceStatus>($"Folder '{segment}' not found for resource key '{key}'");
                    }
                    searchFolder = searchResult.Data!;
                }
            }

            return new ErrorResult<ResourceStatus>($"Failed to add file resource at {key}");
        }

        private Result RemoveResourceFromRegistry(ResourceRegistry registry, string key)
        {
            var segments = key.Split('/');

            var searchFolder = registry.Root;
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (i == segments.Length - 1)
                {
                    var folderResult = RemoveFolderResource(searchFolder, segment);
                    if (folderResult.Success)
                    {
                        return new SuccessResult();
                    }

                    var fileResult = RemoveFileResource(searchFolder, segment);
                    if (fileResult.Success)
                    {
                        return new SuccessResult();
                    }

                    return new ErrorResult($"Failed to delete resource with key '{key}'");
                }
                else
                {
                    var searchResult = FindFolderResource(searchFolder, segment);
                    if (searchResult.Failure)
                    {
                        return new ErrorResult<ResourceState>($"Folder '{segment}' not found for resource keyPath '{key}'");
                    }
                    searchFolder = searchResult.Data!;
                }
            }

            return new ErrorResult($"Failed to delete resource with key '{key}'");
        }
        private static SortedDictionary<string, Resource> GetFolderResourceMap(FolderResource folderResource)
        {
            static void AddFolderToResourceMap(SortedDictionary<string, Resource> resourceMap, FolderResource folder, string folderPath)
            {
                resourceMap[folderPath] = folder;

                foreach (var file in folder.Files)
                {
                    var fileKey = $"{folderPath}/{file.Name}";
                    resourceMap[fileKey] = file;
                }

                foreach (var subFolder in folder.Folders)
                {
                    var subFolderPath = $"{folderPath}/{subFolder.Name}";
                    AddFolderToResourceMap(resourceMap, subFolder, subFolderPath);
                }
            }

            // Add top level files first, then recursively add all child folders
            var resourceMap = new SortedDictionary<string, Resource>();
            foreach (var file in folderResource.Files)
            {
                var fileKey = $"{file.Name}";
                resourceMap[fileKey] = file;
            }
            foreach (var folder in folderResource.Folders)
            {
                var folderKey = $"{folder.Name}";
                AddFolderToResourceMap(resourceMap, folder, folderKey);
            }

            return resourceMap;
        }

        public void WriteResourceRegistryToLog(ResourceRegistry registry)
        {
            static void WriteFolderToLog(FolderResource folder, int depth = 0)
            {
                var parentIndent = new string('\t', depth);
                Log.Information($"{parentIndent}[{folder.Name}]");

                depth++;

                var childIndent = new string('\t', depth);
                foreach (var childFile in folder.Files)
                {
                    Log.Information($"{childIndent}{childFile.Name}");
                }

                foreach (var childFolder in folder.Folders)
                {
                    WriteFolderToLog(childFolder, depth);
                }
            }

            WriteFolderToLog(registry.Root);
        }

        private static Result<FolderResource> FindFolderResource(FolderResource parentFolderResource, string folderName)
        {
            foreach (var folder in parentFolderResource.Folders)
            {
                if (folder.Name == folderName)
                {
                    return new SuccessResult<FolderResource>(folder);
                }
            }
            return new ErrorResult<FolderResource>($"Folder '{folderName}' not found in folder '{parentFolderResource.Name}'");
        }

        private static Result<FileResource> FindFileResource(FolderResource parentFolderResource, string fileName)
        {
            foreach (var file in parentFolderResource.Files)
            {
                if (file.Name == fileName)
                {
                    return new SuccessResult<FileResource>(file);
                }
            }
            return new ErrorResult<FileResource>($"Folder '{fileName}' not found in folder '{parentFolderResource.Name}'");
        }

        private static Result RemoveFolderResource(FolderResource parentFolderResource, string folderName)
        {
            foreach (var folder in parentFolderResource.Folders)
            {
                if (folder.Name == folderName)
                {
                    parentFolderResource.Children.Remove(folder);
                    return new SuccessResult();
                }
            }
            return new ErrorResult($"Failed to delete folder '{folderName}'");
        }

        private Result RemoveFileResource(FolderResource parentFolderResource, string fileName)
        {
            foreach (var file in parentFolderResource.Files)
            {
                if (file.Name == fileName)
                {
                    parentFolderResource.Children.Remove(file);

                    return new SuccessResult();
                }
            }
            return new ErrorResult($"Failed to delete file '{fileName}'");
        }

        public async Task<Result<RegistryUpdateSummary>> UpdateProjectResources(IProject project)
        {
            if (project == null)
            {
                return new ErrorResult<RegistryUpdateSummary>("Failed to update project resources. Project is null.");
            }

            var projectPath = project.ProjectPath;
            var projectFolder = Path.GetDirectoryName(projectPath);

            Guard.IsNotNull(projectFolder);

            var result = await UpdateRegistryFromFolder(project.ResourceRegistry, projectFolder);
            if (result.Failure)
            {
                var error = result as ErrorResult<RegistryUpdateSummary>;
                Log.Error(error!.Message);
                return new ErrorResult<RegistryUpdateSummary>("Failed to update resource registry.");
            }

            var summary = result.Data!;
            if (summary.WasRegistryModified)
            { 
                // Notify about all changes at once to make it easier for client code to handle dependencies between changed resources.
                var message = new ResourcesChangedMessage(summary.Added, summary.Changed, summary.Deleted);
                _messengerService.Send(message);
            }

            return result;
        }

        public Result<string> GetPathForNewResource(IProject project, string resourceName)
        {
            Guard.IsNotNull(project);

            var selectedEntity = InspectorService.SelectedEntity;

            string folder;
            if (selectedEntity == null)
            {
                folder = string.Empty;
            }
            else
            {
                var key = selectedEntity.GetKey();
                if (selectedEntity is FolderResource)
                {
                    folder = key;
                }
                else
                {
                    try
                    {
                        folder = Path.GetDirectoryName(key) ?? string.Empty;
                    }
                    catch (Exception)
                    {
                        folder = string.Empty;
                    }
                }
            }

            var combined = Path.Combine(project.ProjectFolder, folder, resourceName);
            var path = Path.GetFullPath(combined);

            if (Directory.Exists(path))
            {
                return new ErrorResult<string>($"A file or folder already exists at '{path}'");
            }

            return new SuccessResult<string>(path);
        }

        public Result<string> GetResourcePath(IProject project, Resource resource)
        {
            Guard.IsNotNull(project);

            if (resource == null)
            {
                return new ErrorResult<string>("Failed to get newResourcePath for resource because resource is null.");
            }

            var key = resource.GetKey();
            string path;
            if (resource is FolderResource)
            {
                var folder = Path.Combine(project.ProjectFolder, key);
                path = Path.GetFullPath(folder);
            }
            else
            {
                var folder = Path.GetDirectoryName(key);
                Guard.IsNotNull(folder);

                var combined = Path.Combine(project.ProjectFolder, folder, resource.Name);
                path = Path.GetFullPath(combined);
            }

            return new SuccessResult<string>(path);
        }

        public Result<IEntity> FindResourceEntity(IProject project, Guid entityId)
        {
            Guard.IsNotNull(project);

            Resource? FindResourceMatchingId(FolderResource folderResource, Guid entityId)
            {
                foreach (var resource in folderResource.Children)
                {
                    Guard.IsTrue(resource is Resource);

                    var resourceEntity = resource as IEntity;
                    if (resourceEntity == null)
                    {
                        continue;
                    }

                    if (resourceEntity.Id == entityId)
                    {
                        return resource as Resource;
                    }

                    if (resource is FolderResource folder)
                    {
                        var result = FindResourceMatchingId(folder, entityId);
                        if (result != null)
                        {
                            return result;
                        }
                    }
                }
                return null;
            }

            var root = project.ResourceRegistry.Root;
            var matchingResource = FindResourceMatchingId(root, entityId);

            if (matchingResource == null)
            {
                return new ErrorResult<IEntity>($"Resource with id '{entityId}' not found in registry.");
            }

            return new SuccessResult<IEntity>(matchingResource);
        }

        public List<T> FindResourcesOfType<T>(IProject project) where T : Resource
        {
            Guard.IsNotNull(project);

            var resourceType = typeof(T);

            void FindResourcesMatchingType(FolderResource searchFolder, Type resourceType, List<T> searchResults)
            {
                foreach (var resource in searchFolder.Children)
                {
                    Guard.IsTrue(resource is Resource);

                    var resourceEntity = resource as IEntity;
                    if (resourceEntity == null)
                    {
                        continue;
                    }

                    if (resourceEntity.GetType().IsAssignableTo(resourceType))
                    {
                        var r = resourceEntity as T;
                        Guard.IsNotNull(r);

                        searchResults.Add(r);
                    }

                    if (resource is FolderResource folder)
                    {
                        FindResourcesMatchingType(folder, resourceType, searchResults);
                    }
                }
            }

            var resources = new List<T>();
            var root = project.ResourceRegistry.Root;

            FindResourcesMatchingType(root, resourceType, resources);

            return resources;
        }
    }
}
