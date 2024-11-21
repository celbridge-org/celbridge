using Celbridge.Core;
using Celbridge.Entities.Models;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Path = System.IO.Path;

namespace Celbridge.Entities.Services;

/// <summary>
/// Describes the context in which a patch is applied.
/// </summary>
public enum ApplyPatchContext
{
    /// <summary>
    /// Modifying an entity.
    /// </summary>
    Modify,

    /// <summary>
    /// Undoing a previously applied modification.
    /// </summary>
    Undo,

    /// <summary>
    /// Redoing a previously undone modification.
    /// </summary>
    Redo
}

public class EntityService : IEntityService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EntityService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IProjectService _projectService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private readonly ConcurrentDictionary<ResourceKey, Entity> _entityCache = new(); // Cache for entity objects
    private readonly ConcurrentDictionary<ResourceKey, bool> _modifiedEntities = new(); // Track modified entities

    private EntitySchemaRegistry _schemaRegistry;
    private EntityPrototypeRegistry _prototypeRegistry;

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        // Serialize enums as strings rather than numbers
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public EntityService(
        IServiceProvider serviceProvider,
        ILogger<EntityService> logger,
        IMessengerService messengerService,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _messengerService = messengerService;
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;

        _schemaRegistry = serviceProvider.GetRequiredService<EntitySchemaRegistry>();
        _prototypeRegistry = serviceProvider.GetRequiredService<EntityPrototypeRegistry>();

        _messengerService.Register<ResourceRegistryUpdatedMessage>(this, OnResourceRegistryUpdatedMessage);
    }

    public async Task<Result> InitializeAsync()
    {
        var loadSchemasResult = await _schemaRegistry.LoadSchemasAsync();
        if (loadSchemasResult.IsFailure)
        {
            return Result.Fail("Failed to load schemas")
                .WithErrors(loadSchemasResult);
        }

        var loadPrototypesResult = await _prototypeRegistry.LoadPrototypesAsync(_schemaRegistry);
        if (loadPrototypesResult.IsFailure)
        {
            return Result.Fail("Failed to load prototypes")
                .WithErrors(loadPrototypesResult);
        }

        var loadDefaultsResult = await _prototypeRegistry.LoadFileEntityTypesAsync();
        if (loadDefaultsResult.IsFailure)
        {
            return Result.Fail("Failed to load file entity types")
                .WithErrors(loadDefaultsResult);
        }

        return Result.Ok();
    }

    public string GetEntitiesFolderPath()
    {
        var projectDataFolderPath = _projectService.CurrentProject!.ProjectDataFolderPath;
        var path = Path.Combine(projectDataFolderPath, FileNameConstants.EntitiesFolder);
        return path;
    }

    public string GetEntityDataPath(ResourceKey resource)
    {
        var entityDataPath = Path.Combine(GetEntitiesFolderPath(), resource) + ".json";
        entityDataPath = Path.GetFullPath(entityDataPath);
        return entityDataPath;
    }

    public string GetEntityDataRelativePath(ResourceKey resource)
    {
        var relativePath = $"{FileNameConstants.ProjectDataFolder}/{FileNameConstants.EntitiesFolder}/{resource}.json";
        return relativePath;
    }

    public async Task<Result> SaveModifiedEntities()
    {
        foreach (var resourceKey in _modifiedEntities.Keys)
        {
            if (_entityCache.ContainsKey(resourceKey))
            {
                var entity = _entityCache[resourceKey];

                var saveResult = await SaveEntityDataFileAsync(entity);
                if (saveResult.IsFailure)
                {
                    return Result.Fail($"Failed to save entity data for resource: '{resourceKey}'")
                        .WithErrors(saveResult);
                }
            }
        }

        // Clear the modified entities list
        _modifiedEntities.Clear();

        return Result.Ok();
    }

    public T? GetProperty<T>(ResourceKey resource, string propertyPath, T? defaultValue) where T : notnull
    {
        var getResult = GetProperty<T>(resource, propertyPath);
        if (getResult.IsFailure)
        {
            return default;
        }

        return getResult.Value;
    }

    public Result<T> GetProperty<T>(ResourceKey resource, string propertyPath) where T : notnull
    {
        var acquireResult = AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            _logger.LogError(acquireResult.Error);
            return Result<T>.Fail($"Failed to acquire entity for resource '{resource}'")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;
        Guard.IsNotNull(entity);

        var getResult = entity.EntityData.GetProperty<T>(propertyPath);
        if (getResult.IsFailure)
        {
            return Result<T>.Fail($"Failed to get entity property '{propertyPath}' for resource '{resource}'")
                .WithErrors(getResult);
        }

        return getResult;
    }

    public Result<string> GetPropertyAsJSON(ResourceKey resource, string propertyPath)
    {
        var acquireResult = AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            _logger.LogError(acquireResult.Error);
            return Result<string>.Fail($"Failed to acquire entity for resource '{resource}'")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;
        Guard.IsNotNull(entity);

        var getResult = entity.EntityData.GetPropertyAsJSON(propertyPath);
        if (getResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get entity property '{propertyPath}' for resource '{resource}'")
                .WithErrors(getResult);
        }

        return getResult;
    }

    private record SetPropertyOperation(string op, string path, object value);
    public Result<EntityPatchSummary> SetProperty<T>(ResourceKey resource, string propertyPath, T newValue) where T : notnull
    {
        // Set the property by applying a JSON patch
        var operation = new SetPropertyOperation("add", propertyPath, newValue);
        var jsonPatch = JsonSerializer.Serialize(operation, SerializerOptions);
        jsonPatch = $"[{jsonPatch}]";

        var applyResult = ApplyPatch(resource, jsonPatch);
        if (applyResult.IsFailure)
        {
            return Result<EntityPatchSummary>.Fail($"Failed to apply entity patch for resource: {resource}");
        }
        var patchSummary = applyResult.Value;

        return Result<EntityPatchSummary>.Ok(patchSummary);
    }

    public Result<EntityPatchSummary> ApplyPatch(ResourceKey resource, string patch)
    {
        return ApplyPatch(resource, patch, ApplyPatchContext.Modify);
    }

    public Result<bool> UndoPatch(ResourceKey resource)
    {
        var acquireResult = AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            return Result<bool>.Fail($"Failed to acquire entity: {resource}")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;
        Guard.IsNotNull(entity);

        if (entity.UndoStack.Count == 0)
        {
            // Undo stack is empty. Succeed but return false to indicate that no changes were undone.
            return Result<bool>.Ok(false);
        }

        // Pop the next patch summary from the Undo stack and apply it to the entity
        var patchSummary = entity.UndoStack.Pop();
        var reversePatch = patchSummary.ReversePatch;
        Guard.IsNotNull(reversePatch);

        var applyResult = ApplyPatch(resource, reversePatch, ApplyPatchContext.Undo);
        if (applyResult.IsFailure)
        {
            return Result<bool>.Fail($"Failed to apply undo patch to resource: {resource}");
        }

        // Succeed and return true to indicate that a patch was undone.
        return Result<bool>.Ok(true);
    }

    public Result<bool> RedoPatch(ResourceKey resource)
    {
        var acquireResult = AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            return Result<bool>.Fail($"Failed to acquire entity: {resource}")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;
        Guard.IsNotNull(entity);

        if (entity.RedoStack.Count == 0)
        {
            // Redo stack is empty. Succeed but return false to indicate no changes were redone.
            return Result<bool>.Ok(false);
        }

        // Pop the next patch summary from the Redo stack and apply it to the entity
        var patchSummary = entity.RedoStack.Pop();
        var reversePatch = patchSummary.ReversePatch;
        Guard.IsNotNull(reversePatch);

        var applyResult = ApplyPatch(resource, reversePatch, ApplyPatchContext.Redo);
        if (applyResult.IsFailure)
        {
            return Result<bool>.Fail($"Failed to apply redo patch to resource: {resource}");
        }

        // Succeed and return true to indicate that a patch was undone.
        return Result<bool>.Ok(true);
    }

    public Result MoveEntityDataFile(ResourceKey oldResource, ResourceKey newResource)
    {
        try
        {
            if (_entityCache.ContainsKey(oldResource))
            {
                var entity = _entityCache[oldResource];

                var newEntityPath = GetEntityDataPath(newResource);
                entity.SetResourceKey(newResource, newEntityPath);

                _entityCache[newResource] = entity;
                _entityCache.TryRemove(oldResource, out _);

                // Update the modified resources list
                if (_modifiedEntities.ContainsKey(oldResource))
                {
                    _modifiedEntities[newResource] = true;
                    _modifiedEntities.TryRemove(oldResource, out _);
                }

                // Rename the backing JSON file
                string oldEntityPath = GetEntityDataPath(oldResource);
                string newResourcePath = GetEntityDataPath(newResource);
                if (File.Exists(oldEntityPath))
                {
                    var parentFolder = Path.GetDirectoryName(newResourcePath);
                    if (!string.IsNullOrEmpty(parentFolder) &&
                        !Directory.Exists(parentFolder))
                    {
                        Directory.CreateDirectory(parentFolder);
                    }
                    File.Move(oldEntityPath, newResourcePath);
                }
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to remap entities for resource: '{oldResource}' to '{newResource}'")
                .WithException(ex);
        }
    }

    public Result CopyEntityDataFile(ResourceKey sourceResource, ResourceKey destResource)
    {
        try
        {
            if (_entityCache.ContainsKey(destResource))
            {
                // An entity for the destination resource is already cached.
                // This shouldn't be possible, so fail to prevent the operation from proceeding.
                return Result.Fail($"An entity for the destination resource already exists: '{destResource}'");
            }

            var sourceEntityPath = GetEntityDataPath(sourceResource);
            var destEntityPath = GetEntityDataPath(destResource);

            if (!File.Exists(sourceEntityPath))
            {
                // The source entity file does not exist yet, so there's no need to copy it.
                return Result.Ok();
            }

            if (File.Exists(destEntityPath))
            {
                // There is already an entity file for the destination resource.
                // This shouldn't be possible, so we'll log an error and return a failure.
                return Result.Fail($"Destination entity file already exists: '{destEntityPath}'");
            }

            var parentFolder = Path.GetDirectoryName(destEntityPath);
            if (!string.IsNullOrEmpty(parentFolder) &&
                !Directory.Exists(parentFolder))
            {
                Directory.CreateDirectory(parentFolder);
            }

            File.Copy(sourceEntityPath, destEntityPath);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when copying the entity data fom '{sourceResource}' to '{destResource}'")
                .WithException(ex);
        }
    }

    private void OnResourceRegistryUpdatedMessage(object recipient, ResourceRegistryUpdatedMessage message)
    {
        CleanupEntities();
    }

    private Result<Entity> AcquireEntity(ResourceKey resource)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;
        
        var getResourceResult = resourceRegistry.GetResource(resource);
        if (getResourceResult.IsFailure)
        {
            // Fail if the resource does not exist in the registry.
            return Result<Entity>.Fail($"Resource does not exist: '{resource}'")
                .WithErrors(getResourceResult);
        }

        if (_entityCache.ContainsKey(resource))
        {
            var entity = _entityCache[resource];
            return Result<Entity>.Ok(entity);
        }

        try
        {
            // Try to load existing entity data from disk
            string entityDataPath = GetEntityDataPath(resource);

            EntityData? entityData = null;
            if (File.Exists(entityDataPath))
            {
                var getDataResult = LoadEntityDataFile(entityDataPath);
                if (getDataResult.IsSuccess)
                {
                    entityData = getDataResult.Value;
                }
                else
                {
                    _logger.LogError(getDataResult.Error);
                }
            }

            if (entityData is null)
            {
                // We were unable to load an existing entity data, so we need to create a new one
                var acquireResult = AcquireEntityData(resource);
                if (acquireResult.IsSuccess)
                {
                    entityData = acquireResult.Value;
                }
                else
                {
                    // At this point we should always have an EntityData.
                    // This is probably a configuration issue in the application.
                    _logger.LogError(acquireResult.Error);
                    return Result<Entity>.Fail($"Failed to acquire entity data for resource: {resource}");
                }
            }

            // Create the entity and add it to the cache
            var entity = Entity.CreateEntity(resource, entityDataPath, entityData);

            _entityCache[resource] = entity;

            // This line will always create the entity data file, instead of only when the entity is modified.
            // _modifiedEntities.Add(resource);

            return Result<Entity>.Ok(entity);
        }
        catch (Exception ex)
        {
            return Result<Entity>.Fail($"An exception occurred when loading entity data for resource: '{resource}'")
                .WithException(ex);
        }
    }

    private Result<EntityData> AcquireEntityData(ResourceKey resource)
    {
        EntityData? entityPrototype = null;

        // Attempt to find an entity type for the resource's file extension
        var fileExtension = Path.GetExtension(resource.ToString());
        if (!string.IsNullOrEmpty(fileExtension))
        {
            var entityTypes = _prototypeRegistry.GetFileEntityTypes(fileExtension);
            if (entityTypes.Count > 0)
            {
                // Default entity type is the first in the list
                var entityTypeFromConfig = entityTypes[0];

                // Get the prototype for the entity type
                var getPrototypeResult = _prototypeRegistry.GetPrototype(entityTypeFromConfig);
                if (getPrototypeResult.IsFailure)
                {
                    // No prototype was found for the entity type.
                    // This means the prototype is missing from the configuration.
                    return Result<EntityData>.Fail($"Failed to get prototype for entity type: {entityTypeFromConfig}");
                }

                entityPrototype = getPrototypeResult.Value;
            }
        }

        if (entityPrototype == null)
        {
            // This resource does not have a registered entity type, so we need to assign a default entity
            // type based on the resource type (file or folder).

            var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;
            var path = resourceRegistry.GetResourcePath(resource);

            if (Directory.Exists(path))
            {
                // Folder resource
                var getPrototypeResult = _prototypeRegistry.GetPrototype("Folder");
                if (getPrototypeResult.IsFailure)
                {
                    // No Folder prototype was found.
                    // This means the prototype is missing from the configuration.
                    return Result<EntityData>.Fail($"Failed to get prototype for Folder entity type");
                }

                entityPrototype = getPrototypeResult.Value;
            }
            else if (File.Exists(path))
            {
                // File resource
                var getPrototypeResult = _prototypeRegistry.GetPrototype("File");
                if (getPrototypeResult.IsFailure)
                {
                    // No File prototype was found.
                    // This means the prototype is missing from the configuration.
                    return Result<EntityData>.Fail($"Failed to get prototype for File entity type");
                }

                entityPrototype = getPrototypeResult.Value;
            }
        }

        if (entityPrototype is null)
        {
            // We should always have a prototype at this point
            return Result<EntityData>.Fail($"Failed to get entity prototype for resource: {resource}");
        }

        var entityData = entityPrototype.DeepClone();

        return Result<EntityData>.Ok(entityData);
    }

    private Result<EntityData> LoadEntityDataFile(string entityDataPath)
    {
        // Load the EntityData json
        var jsonObject = JsonNode.Parse(File.ReadAllText(entityDataPath)) as JsonObject;
        if (jsonObject is null)
        {
            return Result<EntityData>.Fail($"Failed to parse entity data from file: '{entityDataPath}'");
        }

        // Get the entity type from the JSON object
        if (!jsonObject.TryGetPropertyValue("_entityType", out var entityTypeValue) ||
            entityTypeValue is null ||
            entityTypeValue?.GetValueKind() != System.Text.Json.JsonValueKind.String)
        {
            return Result<EntityData>.Fail($"Entity data does not contain an '_entityType' property: '{entityDataPath}'");
        }

        // Check if this is a valid entity type
        var entityType = entityTypeValue!.ToString();
        if (string.IsNullOrEmpty(entityType))
        {
            return Result<EntityData>.Fail($"'_entityType' property is empty: '{entityDataPath}'");
        }

        // Get the schema for the entity type
        var getSchemaResult = _schemaRegistry.GetSchemaForEntityType(entityType);
        if (getSchemaResult.IsFailure)
        {
            return Result<EntityData>.Fail($"No schema found for entity type: '{entityType}'");
        }

        var entitySchema = getSchemaResult.Value;

        // Validate the data against the schema
        var validateResult = entitySchema.ValidateJsonObject(jsonObject);
        if (validateResult.IsFailure)
        {
            return Result<EntityData>.Fail($"Entity data failed schema validation: '{entityDataPath}'");
        }

        // We've passed validation so now we can create the EntityData object
        var entityData = EntityData.Create(jsonObject, entitySchema);

        return Result<EntityData>.Ok(entityData);
    }

    private async Task<Result> SaveEntityDataFileAsync(Entity entity)
    {
        try
        {
            Guard.IsNotNull(entity.EntityData);

            var jsonContent = JsonSerializer.Serialize(entity.EntityData.JsonObject, SerializerOptions);

            var folder = Path.GetDirectoryName(entity.EntityDataPath);
            Guard.IsNotNull(folder);

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using (var writer = new StreamWriter(entity.EntityDataPath))
            {
                await writer.WriteAsync(jsonContent);
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to save entity data for '{entity.Resource}'")
                .WithException(ex);
        }
    }

    private Result CleanupEntities()
    {
        try
        {
            var projectDataFolderPath = _projectService.CurrentProject!.ProjectDataFolderPath;
            var entitiesFolderPath = Path.Combine(projectDataFolderPath, "Entities");

            if (!Directory.Exists(entitiesFolderPath))
            {
                return Result.Fail("The entities folder does not exist.");
            }

            var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

            // Remove any cached entities whose resources no longer exist on disk
            foreach (var resourceKey in _entityCache.Keys.ToArray())
            {
                var getResult = resourceRegistry.GetResource(resourceKey);
                if (getResult.IsFailure)
                {
                    _entityCache.TryRemove(resourceKey, out _);
                    _modifiedEntities.TryRemove(resourceKey, out _);

                    // Todo: Send a message to let listeners know that this entity is now invalid.
                }
            }

            // Find all the entity files in the Entities folder.
            // Note that an entity .json file may correspond to either a file or folder resource. 
            var entityFiles = Directory.EnumerateFiles(entitiesFolderPath, "*.json", SearchOption.AllDirectories);
            foreach (var entityFile in entityFiles)
            {
                // Get the resource key from the entity file path
                var relativeResourcePath = Path.GetRelativePath(entitiesFolderPath, entityFile);
                relativeResourcePath = Path.ChangeExtension(relativeResourcePath, null);
                var resourceKey = new ResourceKey(relativeResourcePath);

                // Get the resource path (may be a file or a folder)
                var resourcePath = resourceRegistry.GetResourcePath(resourceKey);
                if (Path.Exists(resourcePath))
                {
                    continue;
                }

                _entityCache.TryRemove(resourceKey, out _);
                _modifiedEntities.TryRemove(resourceKey, out _);

                File.Delete(entityFile);
            }

            // Delete any empty folders in the Entities folder
            var folders = Directory.EnumerateDirectories(entitiesFolderPath, "*", SearchOption.AllDirectories);
            foreach (var folder in folders)
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("An exception occurred when cleaning up entities")
                .WithException(ex);
        }
    }

    private Result<EntityPatchSummary> ApplyPatch(ResourceKey resource, string patch, ApplyPatchContext context)
    {
        var acquireResult = AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            return Result<EntityPatchSummary>.Fail($"Failed to acquire entity: {resource}")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;
        Guard.IsNotNull(entity);

        var applyResult = entity.EntityData.ApplyPatch(patch);
        if (applyResult.IsFailure)
        {
            return Result<EntityPatchSummary>.Fail($"Failed to apply patch to entity for resource: {resource}")
                .WithErrors(applyResult);
        }
        var patchSummary = applyResult.Value;

        if (patchSummary.ModifiedPaths.Count > 0)
        {
            _modifiedEntities[resource] = true;

            // Add the patch summary to the requested stack to support undo/redo
            switch (context)
            {
                case ApplyPatchContext.Modify:
                    // Execute: Add patch summary to the Undo stack and clear the Redo stack.
                    entity.UndoStack.Push(patchSummary);
                    entity.RedoStack.Clear();
                    break;
                case ApplyPatchContext.Undo:
                    // Undo: Add patch summary to the Redo stack.
                    entity.RedoStack.Push(patchSummary);
                    break;
                case ApplyPatchContext.Redo:
                    // Undo: Add patch summary to the Undo stack.
                    entity.UndoStack.Push(patchSummary);
                    break;
            }

            var pathsCopy = patchSummary.ModifiedPaths.ToList();
            var message = new EntityChangedMessage(resource, pathsCopy);
            _messengerService.Send(message);
        }

        return Result<EntityPatchSummary>.Ok(patchSummary);
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed objects here
            }

            _disposed = true;
        }
    }

    ~EntityService()
    {
        Dispose(false);
    }
}
