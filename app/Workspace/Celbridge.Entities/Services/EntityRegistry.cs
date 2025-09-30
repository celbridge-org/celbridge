using Celbridge.Entities.Models;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Workspace;
using Json.Schema;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

using Path = System.IO.Path;

namespace Celbridge.Entities.Services;

public class EntityRegistry
{
    private readonly ILogger<EntityRegistry> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IProjectService _projectService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private readonly ConcurrentDictionary<ResourceKey, Entity> _entityCache = new(); // Cache for entity objects
    private readonly ConcurrentDictionary<ResourceKey, bool> _modifiedEntities = new(); // Track modified entities

    private JsonSchema? _entitySchema;
    private ComponentConfigRegistry? _configRegistry;

    public EntityRegistry(
        ILogger<EntityRegistry> logger,
        IMessengerService messengerService,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;
    }

    public Result Initialize(ComponentConfigRegistry configRegistry)
    {
        _configRegistry = configRegistry;

        // Create the entity schema

        var createResult = CreateEntitySchema();
        if (createResult.IsFailure)
        {
            return Result.Fail($"Failed to create entity schema")
                .WithErrors(createResult);
        }
        _entitySchema = createResult.Value;

        return Result.Ok();
    }

    public string GetEntityDataPath(ResourceKey resource)
    {
        var entityDataPath = Path.Combine(GetEntitiesFolderPath(), resource) + ".json";
        entityDataPath = Path.GetFullPath(entityDataPath);
        return entityDataPath;
    }

    public Result CleanupEntities()
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

    private async Task<Result> SaveEntityDataFileAsync(Entity entity)
    {
        try
        {
            Guard.IsNotNull(entity.EntityData);

            var jsonContent = JsonSerializer.Serialize(entity.EntityData.EntityJsonObject, EntityService.SerializerOptions);

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

    public Result<Entity> AcquireEntity(ResourceKey resource)
    {
        Guard.IsNotNull(_entitySchema);
        Guard.IsNotNull(_configRegistry);

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        var getResourceResult = resourceRegistry.GetResource(resource);
        if (getResourceResult.IsFailure)
        {
            // Fail if the resource does not exist in the registry.
            return Result<Entity>.Fail($"Resource does not exist: '{resource}'")
                .WithErrors(getResourceResult);
        }

        if (_entityCache.TryGetValue(resource, out Entity? value))
        {
            var entity = value;
            return Result<Entity>.Ok(entity);
        }

        try
        {
            // Try to load existing entity data from disk
            string entityDataPath = GetEntityDataPath(resource);

            EntityData? entityData = null;
            if (File.Exists(entityDataPath))
            {
                var loadDataResult = EntityUtils.LoadEntityDataFile(entityDataPath, _entitySchema, _configRegistry);
                if (loadDataResult.IsSuccess)
                {
                    entityData = loadDataResult.Value;
                }
                else
                {
                    _logger.LogError(loadDataResult.Error);
                }
            }

            bool createdEntity = false;

            if (entityData is null)
            {
                // We were unable to load an existing entity data, so we need to create a new one
                var createResult = EntityUtils.CreateEntityData(resource, _configRegistry, _entitySchema);
                if (createResult.IsSuccess)
                {
                    entityData = createResult.Value;
                    createdEntity = true;
                }
                else
                {
                    // At this point we should always have an EntityData.
                    // This is probably a configuration issue in the application.
                    _logger.LogError(createResult.Error);
                    return Result<Entity>.Fail($"Failed to acquire entity data for resource: {resource}");
                }
            }

            // Create the entity and add it to the cache
            var entity = Entity.CreateEntity(resource, entityDataPath, entityData);

            _entityCache[resource] = entity;

            // This line will always create the entity data file, instead of only when the entity is modified.
            // _modifiedEntities.Add(resource);

            if (createdEntity)
            {
                // Notify activity service so it can attempt to initialize the new entity with default components.
                var message = new EntityCreatedMessage(resource);
                _messengerService.Send(message);               
            }

            return Result<Entity>.Ok(entity);
        }
        catch (Exception ex)
        {
            return Result<Entity>.Fail($"An exception occurred when loading entity data for resource: '{resource}'")
                .WithException(ex);
        }
    }

    public void MarkModifiedEntity(ResourceKey resource)
    {
        _modifiedEntities[resource] = true;
    }

    public Result<List<int>> GetComponentsOfType(ResourceKey resourceKey, string componentType)
    {
        var acquireResult = AcquireEntity(resourceKey);
        if (acquireResult.IsFailure)
        {
            return Result<List<int>>.Fail($"Failed to acquire entity for resource: {resourceKey}");
        }
        var entity = acquireResult.Value;

        var getIndexResult =  EntityUtils.GetComponentsOfType(entity.EntityData.EntityJsonObject, componentType);
        if (getIndexResult.IsFailure)
        {
            return Result<List<int>>.Fail($"Failed to get component indices for component type: {componentType}");
        }
        var componentIndices = getIndexResult.Value;

        return Result<List<int>>.Ok(componentIndices);
    }

    private Result<JsonSchema> CreateEntitySchema()
    {
        try
        {
            // Build and cache the entity schema
            var builder = new JsonSchemaBuilder()
                .Type(SchemaValueType.Object)
                .Properties(
                    ("_entityVersion", new JsonSchemaBuilder()
                        .Type(SchemaValueType.Integer)
                        .Const(1)
                    ),
                    ("_components", new JsonSchemaBuilder()
                        .Type(SchemaValueType.Array)
                    )
                )
                .Required("_entityVersion", "_components");

            var entitySchema = builder.Build();

            return Result<JsonSchema>.Ok(entitySchema);
        }
        catch (Exception ex)
        {
            return Result<JsonSchema>.Fail("An exception occurred when creating entity schema")
                .WithException(ex);
        }
    }


    private string GetEntitiesFolderPath()
    {
        var projectDataFolderPath = _projectService.CurrentProject!.ProjectDataFolderPath;
        var path = Path.Combine(projectDataFolderPath, ProjectConstants.EntitiesFolder);
        return path;
    }
}
