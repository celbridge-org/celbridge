using Celbridge.Entities.Models;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Workspace;
using Json.More;
using Json.Patch;
using Json.Pointer;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Celbridge.Entities.Services;

public class EntityService : IEntityService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EntityService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private ComponentConfigRegistry _configRegistry;
    private ComponentProxyService _componentProxyService;
    private EntityRegistry _entityRegistry;

    private static long _undoGroupId;

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
        _workspaceWrapper = workspaceWrapper;

        _configRegistry = serviceProvider.GetRequiredService<ComponentConfigRegistry>();
        _entityRegistry = serviceProvider.GetRequiredService<EntityRegistry>();
        _componentProxyService = serviceProvider.GetRequiredService<ComponentProxyService>();

        _messengerService.Register<ResourceRegistryUpdatedMessage>(this, OnResourceRegistryUpdatedMessage);
    }

    public async Task<Result> InitializeAsync()
    {
        try
        {
            // Initialize the component proxy service
            var initProxyResult = _componentProxyService.Initialize();
            if (initProxyResult.IsFailure)
            {
                return Result.Fail("Failed to initialize component proxy service")
                    .WithErrors(initProxyResult);
            }

            var initConfigResult = _configRegistry.Initialize();
            if (initConfigResult.IsFailure)
            {
                return Result.Fail("Failed to initialize the component config registry")
                    .WithErrors(initConfigResult);
            }

            var initEntitiesResult = _entityRegistry.Initialize(_configRegistry);
            if (initEntitiesResult.IsFailure)
            {
                return Result.Fail("Failed to initialize entity registry")
                    .WithErrors(initEntitiesResult);
            }

            await Task.CompletedTask;

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when initializing the entity service")
                .WithException(ex);
        }
    }

    public string GetEntityDataPath(ResourceKey resource)
    {
        return _entityRegistry.GetEntityDataPath(resource);
    }

    public string GetEntityDataRelativePath(ResourceKey resource)
    {
        var relativePath = $"{FileNameConstants.ProjectDataFolder}/{FileNameConstants.EntitiesFolder}/{resource}.json";
        return relativePath;
    }

    public async Task<Result> SaveEntitiesAsync()
    {
        return await _entityRegistry.SaveModifiedEntities();
    }

    public Result MoveEntityDataFile(ResourceKey oldResource, ResourceKey newResource)
    {
        return _entityRegistry.MoveEntityDataFile(oldResource, newResource);
    }

    public Result CopyEntityDataFile(ResourceKey sourceResource, ResourceKey destResource)
    {
        return _entityRegistry.CopyEntityDataFile(sourceResource, destResource);
    }

    public Result AddComponent(ComponentKey componentKey, string componentType)
    {
        return AddComponent(componentKey, componentType, 0);
    }

    private Result AddComponent(ComponentKey componentKey, string componentType, long undoGroupId)
    {
        // Acquire the entity for the specified resource

        var acquireResult = _entityRegistry.AcquireEntity(componentKey.Resource);
        if (acquireResult.IsFailure)
        {
            return Result.Fail($"Failed to acquire entity for component: {componentKey}")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;

        // Acquire the component config 

        var getConfigResult = _configRegistry.GetComponentConfig(componentType);
        if (getConfigResult.IsFailure)
        {
            return Result.Fail($"Failed to get component config for component type: {componentType}")
                .WithErrors(getConfigResult);
        }
        var config = getConfigResult.Value;

        // Instantiate the prototype

        var prototypeInstance = config.Prototype.AsNode()!;

        // Apply a patch operation to add the component to the entity

        var componentPointer = JsonPointer.Create("_components", componentKey.ComponentIndex);
        var patchOperation = PatchOperation.Add(componentPointer, prototypeInstance);

        var applyResult = ApplyPatchOperation(entity, patchOperation, undoGroupId);
        if (applyResult.IsFailure)
        {
            return Result.Fail($"Failed to apply patch to add component: '{componentKey}'")
                .WithErrors(applyResult);
        }

        _logger.LogDebug($"Added component: '{componentKey}'");

        return Result.Ok();
    }

    public Result RemoveComponent(ComponentKey componentKey)
    {
        return RemoveComponent(componentKey, 0);
    }

    private Result RemoveComponent(ComponentKey componentKey, long undoGroupId)
    {
        // Acquire the entity for the specified resource

        var acquireResult = _entityRegistry.AcquireEntity(componentKey.Resource);
        if (acquireResult.IsFailure)
        {
            return Result.Fail($"Failed to acquire entity: '{componentKey}'")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;

        // Apply a patch to remove the component at the specified index

        var componentPointer = JsonPointer.Create("_components", componentKey.ComponentIndex);
        var patchOperation = PatchOperation.Remove(componentPointer);

        var applyResult = ApplyPatchOperation(entity, patchOperation, undoGroupId);
        if (applyResult.IsFailure)
        {
            return Result.Fail($"Failed to apply patch to remove entity component: '{componentKey}'")
                .WithErrors(applyResult);
        }

        _logger.LogDebug($"Removed entity component: '{componentKey}'");

        return Result.Ok();
    }

    public Result ReplaceComponent(ComponentKey componentKey, string componentType)
    {
        // Assing a new undo group id so the operations are undone in a single step
        _undoGroupId++;

        // Insert a new component at the specified index
        var addResult = AddComponent(componentKey, componentType, _undoGroupId);
        if (addResult.IsFailure)
        {
            return Result.Fail($"Failed to add component '{componentType}' at '{componentKey}'");
        }

        // Remove the component that was moved right in the list
        var removeKey = new ComponentKey(componentKey.Resource, componentKey.ComponentIndex + 1);
        var removeResult = RemoveComponent(removeKey, _undoGroupId);
        if (removeResult.IsFailure)
        {
            return Result.Fail($"Failed to remove component: '{removeKey}'");
        }

        return Result.Ok();
    }

    public Result CopyComponent(ResourceKey resource, int sourceComponentIndex, int destComponentIndex)
    {
        if (sourceComponentIndex == destComponentIndex)
        {
            // No need to copy the component if the source and destination indices are the same
            return Result.Ok();
        }

        // Acquire the entity for the specified resource

        var acquireResult = _entityRegistry.AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            return Result.Fail($"Failed to acquire entity: {resource}")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;

        // Get the component at the source index

        var sourceComponentPointer = JsonPointer.Create("_components", sourceComponentIndex);
        var getComponentResult = entity.EntityData.GetPropertyAsJsonNode(sourceComponentPointer);
        if (getComponentResult.IsFailure)
        {
            return Result.Fail($"Failed to get component at index '{sourceComponentIndex}' for resource: {resource}")
                .WithErrors(getComponentResult);
        }
        var sourceComponentNode = getComponentResult.Value;

        // Apply a patch operation to add the component to the entity

        var destComponentPointer = JsonPointer.Create("_components", destComponentIndex);
        var patchOperation = PatchOperation.Add(destComponentPointer, sourceComponentNode);

        var applyResult = ApplyPatchOperation(entity, patchOperation, 0);
        if (applyResult.IsFailure)
        {
            return Result.Fail($"Failed to apply patch to add component to entity: {resource}")
                .WithErrors(applyResult);
        }

        _logger.LogDebug($"Copied entity component from '{sourceComponentIndex}' to '{destComponentIndex}' for resource '{resource}'");

        return Result.Ok();
    }

    public Result MoveComponent(ResourceKey resource, int sourceComponentIndex, int destComponentIndex)
    {
        if (sourceComponentIndex == destComponentIndex)
        {
            // No need to copy the component if the source and destination indices are the same
            return Result.Ok();
        }

        // Acquire the entity for the specified resource

        var acquireResult = _entityRegistry.AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            return Result.Fail($"Failed to acquire entity: {resource}")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;

        // Get the component at the source index

        var sourceComponentPointer = JsonPointer.Create("_components", sourceComponentIndex);
        var getComponentResult = entity.EntityData.GetPropertyAsJsonNode(sourceComponentPointer);
        if (getComponentResult.IsFailure)
        {
            return Result.Fail($"Failed to get component at index '{sourceComponentIndex}' for resource: {resource}")
                .WithErrors(getComponentResult);
        }
        var sourceComponentNode = getComponentResult.Value;

        // Both operations have the same non-zero undo group, so they will be undone together
        _undoGroupId++;

        // Apply a patch operation to remove the source component

        var removeOperation = PatchOperation.Remove(sourceComponentPointer);
        var removeResult = ApplyPatchOperation(entity, removeOperation, _undoGroupId);
        if (removeResult.IsFailure)
        {
            return Result.Fail($"Failed to apply patch to remove source component from entity: {resource}")
                .WithErrors(removeResult);
        }

        // Apply a patch operation to add the destination component

        var destComponentPointer = JsonPointer.Create("_components", destComponentIndex);
        var addOperation = PatchOperation.Add(destComponentPointer, sourceComponentNode);

        var applyResult = ApplyPatchOperation(entity, addOperation, _undoGroupId);
        if (removeResult.IsFailure)
        {
            return Result.Fail($"Failed to apply patch to add destination component to entity: {resource}")
                .WithErrors(applyResult);
        }

        _logger.LogDebug($"Moved entity component from '{sourceComponentIndex}' to '{destComponentIndex}' for resource '{resource}'");

        return Result.Ok();
    }

    public int GetComponentCount(ResourceKey resource)
    {
        // Acquire the entity for the specified resource
        var acquireResult = _entityRegistry.AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            return 0;
        }
        var entity = acquireResult.Value;

        // Get the component count
        var getCountResult = entity.EntityData.GetComponentCount();
        if (getCountResult.IsFailure)
        {
            return 0;
        }
        var componentCount = getCountResult.Value;

        return componentCount;
    }

    public Result<string> GetComponentType(ComponentKey componentKey)
    {
        // Acquire the entity for the specified resource

        var acquireResult = _entityRegistry.AcquireEntity(componentKey.Resource);
        if (acquireResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to acquire entity: '{componentKey}'")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;

        // Get the component type

        var componentTypePointer = JsonPointer.Create("_components", componentKey.ComponentIndex, "_componentType");
        var getTypeResult = entity.EntityData.GetProperty<string>(componentTypePointer);
        if (getTypeResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get component type: '{componentKey}'")
                .WithErrors(getTypeResult);
        }
        var typeAndVersion = getTypeResult.Value;

        var parseResult = EntityUtils.ParseComponentTypeAndVersion(typeAndVersion);
        if (parseResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to parse component type and version: '{typeAndVersion}'")
                .WithErrors(parseResult);
        }

        var (componentType, _) = parseResult.Value;

        return Result<string>.Ok(componentType);
    }

    public Result<ComponentSchema> GetComponentSchema(string componentType)
    {
        // Get the component config

        var getConfigResult = _configRegistry.GetComponentConfig(componentType);
        if (getConfigResult.IsFailure)
        {
            return Result<ComponentSchema>.Fail($"Failed to get component config for component type: '{componentType}'")
                .WithErrors(getConfigResult);
        }
        var componentConfig = getConfigResult.Value;

        // Return the component schema

        return Result<ComponentSchema>.Ok(componentConfig.ComponentSchema);
    }

    public Result<IComponentProxy> GetComponent(ComponentKey componentKey)
    {
        return _componentProxyService.GetComponent(componentKey);
    }

    public Result<IComponentProxy> GetComponentOfType(ResourceKey resource, string componentType)
    {
        return _componentProxyService.GetComponentOfType(resource, componentType);
    }

    public Result<IReadOnlyList<IComponentProxy>> GetComponents(ResourceKey resource)
    {
        return _componentProxyService.GetComponents(resource, string.Empty);
    }

    public Result<IReadOnlyList<IComponentProxy>> GetComponentsOfType(ResourceKey resource, string componentType)
    {
        return _componentProxyService.GetComponents(resource, componentType);
    }

    public T? GetProperty<T>(ComponentKey componentKey, string propertyPath, T? defaultValue) where T : notnull
    {
        var getResult = GetProperty<T>(componentKey, propertyPath);
        if (getResult.IsFailure)
        {
            return default;
        }

        return getResult.Value;
    }

    public Result<T> GetProperty<T>(ComponentKey componentKey, string propertyPath) where T : notnull
    {
        var acquireResult = _entityRegistry.AcquireEntity(componentKey.Resource);
        if (acquireResult.IsFailure)
        {
            _logger.LogError(acquireResult.Error);
            return Result<T>.Fail($"Failed to acquire entity for component '{componentKey}'")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;
        Guard.IsNotNull(entity);

        var propertyPointer = GetPropertyPointer(componentKey.ComponentIndex, propertyPath);

        var getResult = entity.EntityData.GetProperty<T>(propertyPointer);
        if (getResult.IsFailure)
        {
            return Result<T>.Fail($"Failed to get property '{propertyPath}' for component '{componentKey}'")
                .WithErrors(getResult);
        }

        return getResult;
    }

    public Result<string> GetPropertyAsJson(ComponentKey componentKey, string propertyPath)
    {
        var acquireResult = _entityRegistry.AcquireEntity(componentKey.Resource);
        if (acquireResult.IsFailure)
        {
            _logger.LogError(acquireResult.Error);
            return Result<string>.Fail($"Failed to acquire entity: '{componentKey}'")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;
        Guard.IsNotNull(entity);

        var propertyPointer = GetPropertyPointer(componentKey.ComponentIndex, propertyPath);

        var getResult = entity.EntityData.GetPropertyAsJsonNode(propertyPointer);
        if (getResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get property '{propertyPath}' for component '{componentKey}'")
                .WithErrors(getResult);
        }

        var jsonNode = getResult.Value;
        var jsonString = jsonNode.ToJsonString();

        return Result<string>.Ok(jsonString);
    }

    public Result SetProperty<T>(ComponentKey componentKey, string propertyPath, T newValue, bool insert) where T : notnull
    {
        var acquireResult = _entityRegistry.AcquireEntity(componentKey.Resource);
        if (acquireResult.IsFailure)
        {
            return Result.Fail($"Failed to acquire entity: '{componentKey}'")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;
        Guard.IsNotNull(entity);

        // Construct a patch operation to set the property

        var propertyPointer = GetPropertyPointer(componentKey.ComponentIndex, propertyPath);
        var jsonValue = JsonSerializer.SerializeToNode(newValue, SerializerOptions);

        PatchOperation operation;
        if (insert)
        {
            operation = PatchOperation.Add(propertyPointer, jsonValue);
        }
        else
        {
            operation = PatchOperation.Replace(propertyPointer, jsonValue);
        }

        var applyResult = ApplyPatchOperation(entity, operation, 0);
        if (applyResult.IsFailure)
        {
            return Result.Fail($"Failed to apply entity patch for component: '{componentKey}'")
                .WithErrors(applyResult);
        }

        _logger.LogDebug($"Set property '{propertyPath}' for component '{componentKey}'");

        return Result.Ok();
    }

    public int GetUndoCount(ResourceKey resource)
    {
        var acquireResult = _entityRegistry.AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            return 0;
        }
        var entity = acquireResult.Value;

        return entity.UndoStack.Count;
    }

    public Result UndoEntity(ResourceKey resource)
    {
        var acquireResult = _entityRegistry.AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            return Result<bool>.Fail($"Failed to acquire entity: {resource}")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;
        Guard.IsNotNull(entity);

        if (entity.UndoStack.Count == 0)
        {
            return Result.Fail($"No undo operations available for resource: {resource}");
        }

        // Pop the next patch summary from the Undo stack and apply it to the entity
        var patchSummary = entity.UndoStack.Pop();
        var reversePatchOperation = patchSummary.ReverseOperation;
        var undoGroupId = patchSummary.UndoGroupId;
        Guard.IsNotNull(reversePatchOperation);

        var applyResult = ApplyPatchOperation(entity, reversePatchOperation, undoGroupId, PatchContext.Undo);
        if (applyResult.IsFailure)
        {
            return Result.Fail($"Failed to apply undo patch to resource: {resource}")
                .WithErrors(applyResult);
        }

        // If the next patch summary in the Undo stack has the same UndoGroup, then undo it as well.
        // The easiest way to do this is to call UndoEntity recursively.
        if (patchSummary.UndoGroupId != 0 &&
            entity.UndoStack.Count != 0 &&
            entity.UndoStack.Peek().UndoGroupId == undoGroupId)
        {
            return UndoEntity(resource);
        }

        // _logger.LogDebug($"Undo entity: {resource}");

        return Result.Ok();
    }

    public int GetRedoCount(ResourceKey resource)
    {
        var acquireResult = _entityRegistry.AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            return 0;
        }
        var entity = acquireResult.Value;

        return entity.RedoStack.Count;
    }

    public Result RedoEntity(ResourceKey resource)
    {
        var acquireResult = _entityRegistry.AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            return Result.Fail($"Failed to acquire entity: {resource}")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;
        Guard.IsNotNull(entity);

        if (entity.RedoStack.Count == 0)
        {
            return Result.Fail($"No redo operations available for resource: {resource}");
        }

        // Pop the next patch summary from the Redo stack and apply it to the entity
        var patchSummary = entity.RedoStack.Pop();
        var reverseOperation = patchSummary.ReverseOperation;
        var undoGroupId = patchSummary.UndoGroupId;
        Guard.IsNotNull(reverseOperation);

        var applyResult = ApplyPatchOperation(entity, reverseOperation, undoGroupId, PatchContext.Redo);
        if (applyResult.IsFailure)
        {
            return Result.Fail($"Failed to apply redo patch to resource: {resource}");
        }

        // If the next patch summary in the Redo stack has the same UndoGroup, then redo it as well.
        // The easiest way to do this is to call RedoEntity recursively.
        if (patchSummary.UndoGroupId != 0 &&
            entity.RedoStack.Count != 0 &&
            entity.RedoStack.Peek().UndoGroupId == undoGroupId)
        {
            return RedoEntity(resource);
        }

        // _logger.LogDebug($"Redo entity: {resource}");

        return Result.Ok();
    }

    public bool HasTag(ResourceKey resource, string tag)
    {
        var acquireResult = _entityRegistry.AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            return false;
        }
        var entity = acquireResult.Value;

        return entity.EntityData.Tags.Contains(tag);
    }

    public List<string> GetAllComponentTypes()
    {
        var componentTypes = _configRegistry.ComponentConfigs.Keys.ToList();
        componentTypes.Sort();
        return componentTypes;
    }

    public Result<string> GetActivity(ResourceKey resource)
    {
        // Acquire the entity
        var acquireResult = _entityRegistry.AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to acquire entity: {resource}")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;

        // Lookup and return the activity
        var activity = entity.EntityData.GetActivity();
        return Result<string>.Ok(activity);
    }

    public Result SetActivity(ResourceKey resource, string activity)
    {
        // Acquire the entity
        var acquireResult = _entityRegistry.AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            return Result.Fail($"Failed to acquire entity: {resource}")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;

        // Set the activity in the EntityData
        var existingActivity = entity.EntityData.GetActivity();
        if (existingActivity != activity)
        {
            // Set the activity and mark the entity as modified
            entity.EntityData.SetActivity(activity);
            _entityRegistry.MarkModifiedEntity(resource);
        }

        return Result.Ok();
    }

    public Result<IComponentEditor> CreateComponentEditor(IComponentProxy componentProxy)
    {
        // Acquire the config for this component
        var componentType = componentProxy.Schema.ComponentType;
        var getConfigResult = _configRegistry.GetComponentConfig(componentType);
        if (getConfigResult.IsFailure)
        {
            return Result<IComponentEditor>.Fail($"Failed to get component config for component type: '{componentType}'")
                .WithErrors(getConfigResult);
        } 
        var config = getConfigResult.Value;

        // Instantiate the component editor

        var editorType = config.ComponentEditorType;
        var editor = _serviceProvider.GetService(editorType) as IComponentEditor;
        if (editor is null)
        {
            return Result<IComponentEditor>.Fail($"Failed to created component editor for component type: {componentType}");
        }

        var initResult = editor.Initialize(componentProxy);
        if (initResult.IsFailure)
        {
            return Result<IComponentEditor>.Fail($"Failed to initialize component editor")
                .WithErrors(initResult);
        }

        return Result<IComponentEditor>.Ok(editor);
    }

    private static JsonPointer GetPropertyPointer(int componentIndex, string propertyPath)
    {
        var trimmedPath = propertyPath.TrimStart('/');
        var jsonPointer = JsonPointer.Create("_components", componentIndex, trimmedPath);
        return jsonPointer;
    }

    private void OnResourceRegistryUpdatedMessage(object recipient, ResourceRegistryUpdatedMessage message)
    {
        _entityRegistry.CleanupEntities();
    }

    private Result ApplyPatchOperation(Entity entity, PatchOperation patchOperation, long undoGroupId, PatchContext context = PatchContext.Modify)
    {
        if (patchOperation.Op == OperationType.Unknown ||
            patchOperation.Op == OperationType.Copy ||
            patchOperation.Op == OperationType.Move ||
            patchOperation.Op == OperationType.Test)
        {
            return Result.Fail($"Patch operation is not supported: {patchOperation.Op}");
        }

        var applyResult = entity.ApplyPatchOperation(patchOperation, _configRegistry, undoGroupId, context);
        if (applyResult.IsFailure)
        {
            return Result.Fail($"Failed to apply entity patch for resource: '{entity.Resource}'")
                .WithErrors(applyResult);
        }
        var patchSummary = applyResult.Value;

        if (patchSummary.ComponentChangedMessage is not null)
        {
            // Mark the entity as needing to be saved
            _entityRegistry.MarkModifiedEntity(entity.Resource);

            // Notify listeners about the component changes
            _messengerService.Send(patchSummary.ComponentChangedMessage);
        }

        _logger.LogDebug($"Modified entity: {patchSummary.ComponentChangedMessage}");

        return Result.Ok();
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

                // Uninitialize the component proxy service
                var uninitializeResult = _componentProxyService.Uninitialize();
                if (uninitializeResult.IsFailure)
                {
                    _logger.LogError(uninitializeResult.Error);
                }
            }

            _disposed = true;
        }
    }

    ~EntityService()
    {
        Dispose(false);
    }
}
