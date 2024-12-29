using Celbridge.Core;
using Celbridge.Entities.Models;
using Celbridge.Projects;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using Json.More;
using Json.Schema;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

using Path = System.IO.Path;

namespace Celbridge.Entities.Services;

public class EntityRegistry
{
    private const string DefaultComponentsFile = "DefaultComponents.json";

    private readonly ILogger<EntityRegistry> _logger;
    private readonly IProjectService _projectService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private readonly ConcurrentDictionary<ResourceKey, Entity> _entityCache = new(); // Cache for entity objects
    private readonly ConcurrentDictionary<ResourceKey, bool> _modifiedEntities = new(); // Track modified entities
    private readonly Dictionary<string, List<string>> _defaultComponents = new();

    private JsonSchema? _entitySchema;
    private ComponentSchemaRegistry? _schemaRegistry;

    public EntityRegistry(
        ILogger<EntityRegistry> logger,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task<Result> Initialize(JsonSchema entitySchema, ComponentSchemaRegistry schemaRegistry)
    {
        _entitySchema = entitySchema;
        _schemaRegistry = schemaRegistry;

        return await LoadDefaultComponentsAsync();
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
        Guard.IsNotNull(_schemaRegistry);

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
                var getDataResult = EntityUtils.LoadEntityDataFile(entityDataPath, _entitySchema, _schemaRegistry);
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
                var createResult = CreateEntityData(resource);
                if (createResult.IsSuccess)
                {
                    entityData = createResult.Value;
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

    private string GetEntitiesFolderPath()
    {
        var projectDataFolderPath = _projectService.CurrentProject!.ProjectDataFolderPath;
        var path = Path.Combine(projectDataFolderPath, FileNameConstants.EntitiesFolder);
        return path;
    }

    private async Task<Result> LoadDefaultComponentsAsync()
    {
        try
        {
            var configFolder = await Package.Current.InstalledLocation.GetFolderAsync(EntityConstants.ComponentConfigFolder);
            var jsonFile = await configFolder.GetFileAsync(DefaultComponentsFile);

            var content = await FileIO.ReadTextAsync(jsonFile);

            using var jsonDoc = JsonDocument.Parse(content);

            foreach (var property in jsonDoc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array)
                {
                    return Result.Fail($"Expected object value for property: {property.Name}");
                }

                var list = new List<string>();
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        return Result.Fail($"Expected string value for property: {property.Name}");
                    }

                    list.Add(item.GetString()!);
                }

                _defaultComponents[property.Name] = list;
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when loading default components file")
                .WithException(ex);
        }
    }

    private Result<EntityData> CreateEntityData(ResourceKey resource)
    {
        Guard.IsNotNull(_defaultComponents);
        Guard.IsNotNull(_schemaRegistry);
        Guard.IsNotNull(_entitySchema);

        var entityJsonObject = new JsonObject
        {
            ["_entityVersion"] = 1,
            ["_components"] = new JsonArray()
        };

        // Add default components based on the resource's file extension

        var fileExtension = Path.GetExtension(resource.ToString());
        if (string.IsNullOrEmpty(fileExtension))
        {
            // Todo: Handle resources without file extensions and folder resources
            return Result<EntityData>.Fail($"Resource does not have a file extension: '{resource}'");
        }

        if (_defaultComponents.TryGetValue(fileExtension, out var defaultComponents))
        {
            foreach (var componentType in defaultComponents)
            {
                var getSchemaResult = _schemaRegistry.GetSchemaForComponentType(componentType);
                if (getSchemaResult.IsFailure)
                {
                    return Result<EntityData>.Fail($"Failed to get component schema for component type: {componentType}")
                        .WithErrors(getSchemaResult);
                }
                var schema = getSchemaResult.Value;

                // The component prototype was validated against the component schemas at startup, so we can assume
                // it's valid and add a clone of the prototype to the entity data.

                var componentObject = schema.Prototype.AsNode();
                Guard.IsNotNull(componentObject);

                var componentsArray = entityJsonObject["_components"] as JsonArray;
                Guard.IsNotNull(componentsArray);

                componentsArray.Add(componentObject);
            }
        }

        var evaluateResult = _entitySchema.Evaluate(entityJsonObject);
        if (!evaluateResult.IsValid)
        {
            return Result<EntityData>.Fail($"Failed to create entity data. Schema validation error: {resource}");
        }

        var getTagsResult = EntityUtils.GetAllComponentTags(entityJsonObject, _schemaRegistry);
        if (getTagsResult.IsFailure)
        {
            return Result<EntityData>.Fail($"Failed to get component tags for entity data: {resource}")
                .WithErrors(getTagsResult);
        }
        var tags = getTagsResult.Value;

        var entityData = EntityData.Create(entityJsonObject, _entitySchema, tags);

        return Result<EntityData>.Ok(entityData);
    }
}
