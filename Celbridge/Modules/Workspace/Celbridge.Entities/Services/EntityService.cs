using Celbridge.Core;
using Celbridge.Entities.Models;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using Json.Patch;
using Json.Pointer;
using Json.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Celbridge.Entities.Services;

public class EntityService : IEntityService, IDisposable
{
    public const string ComponentConfigFolder = "ComponentConfig";
    public const string SchemasFolder = "Schemas";
    public const string PrototypesFolder = "Prototypes";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EntityService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private ComponentSchemaRegistry _schemaRegistry;
    private ComponentPrototypeRegistry _prototypeRegistry;
    private EntityRegistry _entityRegistry;

    private readonly Dictionary<string, List<string>> _defaultComponents = new();
    private JsonSchema? _entitySchema;

    private static long _undoGroup;

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

        _schemaRegistry = serviceProvider.GetRequiredService<ComponentSchemaRegistry>();
        _prototypeRegistry = serviceProvider.GetRequiredService<ComponentPrototypeRegistry>();
        _entityRegistry = serviceProvider.GetRequiredService<EntityRegistry>();

        _messengerService.Register<ResourceRegistryUpdatedMessage>(this, OnResourceRegistryUpdatedMessage);
    }

    public async Task<Result> InitializeAsync()
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

            _entitySchema = builder.Build();
            Guard.IsNotNull(_entitySchema);

            var loadSchemasResult = await _schemaRegistry.LoadComponentSchemasAsync();
            if (loadSchemasResult.IsFailure)
            {
                return Result.Fail("Failed to load component schemas")
                    .WithErrors(loadSchemasResult);
            }

            var loadPrototypesResult = await _prototypeRegistry.LoadComponentPrototypesAsync(_schemaRegistry);
            if (loadPrototypesResult.IsFailure)
            {
                return Result.Fail("Failed to load component prototypes")
                    .WithErrors(loadPrototypesResult);
            }

            var loadDefaultsResult = await _entityRegistry.Initialize(_entitySchema, _prototypeRegistry, _schemaRegistry);
            if (loadDefaultsResult.IsFailure)
            {
                return Result.Fail("Failed to load file default components")
                    .WithErrors(loadDefaultsResult);
            }

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

    public async Task<Result> SaveModifiedEntities()
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

    public Result AddComponent(ResourceKey resource, string componentType, int componentIndex)
    {
        // Acquire the entity for the specified resource

        var acquireResult = _entityRegistry.AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            return Result.Fail($"Failed to acquire entity: {resource}")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;

        // Acquire the schema for the specified componentType

        var componentSchemaResult = _schemaRegistry.GetSchemaForComponentType(componentType);
        if (componentSchemaResult.IsFailure)
        {
            return Result.Fail($"Failed to get schema for component type: {componentType}")
                .WithErrors(componentSchemaResult);
        }
        var componentSchema = componentSchemaResult.Value;

        // Acquire the prototype for the specified componentType

        var getPrototypeResult = _prototypeRegistry.GetPrototype(componentType);
        if (getPrototypeResult.IsFailure)
        {
            return Result.Fail($"Failed to acquire component prototype for component type: {componentType}")
                .WithErrors(getPrototypeResult);
        }
        var prototype = getPrototypeResult.Value;

        // Apply a patch operation to add the component to the entity

        var componentPointer = JsonPointer.Create("_components", componentIndex);
        var patchOperation = PatchOperation.Add(componentPointer, prototype.JsonObject);

        var applyResult = ApplyPatchOperation(entity, patchOperation, 0);
        if (applyResult.IsFailure)
        {
            return Result.Fail($"Failed to apply patch to add component to entity: {resource}")
                .WithErrors(applyResult);
        }

        return Result.Ok();
    }

    public Result RemoveComponent(ResourceKey resource, int componentIndex)
    {
        // Acquire the entity for the specified resource

        var acquireResult = _entityRegistry.AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            return Result.Fail($"Failed to acquire entity: {resource}")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;

        // Apply a patch to remove the component at the specified index

        var componentPointer = JsonPointer.Create("_components", componentIndex);
        var patchOperation = PatchOperation.Remove(componentPointer);

        var applyResult = ApplyPatchOperation(entity, patchOperation, 0);
        if (applyResult.IsFailure)
        {
            return Result.Fail($"Failed to apply patch to remove entity component at index '{componentIndex}' for resource: {resource}")
                .WithErrors(applyResult);
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
        _undoGroup++;

        // Apply a patch operation to remove the source component

        var removeOperation = PatchOperation.Remove(sourceComponentPointer);
        var removeResult = ApplyPatchOperation(entity, removeOperation, _undoGroup);
        if (removeResult.IsFailure)
        {
            return Result.Fail($"Failed to apply patch to remove source component from entity: {resource}")
                .WithErrors(removeResult);
        }

        // Apply a patch operation to add the destination component

        var destComponentPointer = JsonPointer.Create("_components", destComponentIndex);
        var addOperation = PatchOperation.Add(destComponentPointer, sourceComponentNode);

        var applyResult = ApplyPatchOperation(entity, addOperation, _undoGroup);
        if (removeResult.IsFailure)
        {
            return Result.Fail($"Failed to apply patch to add destination component to entity: {resource}")
                .WithErrors(applyResult);
        }

        return Result.Ok();
    }

    public Result<List<int>> GetComponentsOfType(ResourceKey resourceKey, string componentType)
    {
        return _entityRegistry.GetComponentsOfType(resourceKey, componentType);
    }

    public T? GetProperty<T>(ResourceKey resource, int componentIndex, string propertyPath, T? defaultValue) where T : notnull
    {
        var getResult = GetProperty<T>(resource, componentIndex, propertyPath);
        if (getResult.IsFailure)
        {
            return default;
        }

        return getResult.Value;
    }

    public T? GetProperty<T>(ResourceKey resource, string componentType, string propertyPath, T? defaultValue) where T : notnull
    {
        var getComponentsResult = GetComponentsOfType(resource, componentType);
        if (getComponentsResult.IsFailure)
        {
            return default;
        }
        var componentIndices = getComponentsResult.Value;

        if (componentIndices.Count < 1)
        {
            return default;
        }
        var componentIndex = componentIndices[0];

        var getPropertyResult = GetProperty<T>(resource, componentIndex, propertyPath);
        if (getPropertyResult.IsFailure)
        {
            return default;
        }

        return getPropertyResult.Value;
    }

    public Result<T> GetProperty<T>(ResourceKey resource, int componentIndex, string propertyPath) where T : notnull
    {
        var acquireResult = _entityRegistry.AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            _logger.LogError(acquireResult.Error);
            return Result<T>.Fail($"Failed to acquire entity for resource '{resource}'")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;
        Guard.IsNotNull(entity);

        var propertyPointer = GetPropertyPointer(componentIndex, propertyPath);

        var getResult = entity.EntityData.GetProperty<T>(propertyPointer);
        if (getResult.IsFailure)
        {
            return Result<T>.Fail($"Failed to get component property '{propertyPath}' for resource '{resource}'")
                .WithErrors(getResult);
        }

        return getResult;
    }

    public Result<string> GetPropertyAsJson(ResourceKey resource, int componentIndex, string propertyPath)
    {
        var acquireResult = _entityRegistry.AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            _logger.LogError(acquireResult.Error);
            return Result<string>.Fail($"Failed to acquire entity for resource '{resource}'")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;
        Guard.IsNotNull(entity);

        var propertyPointer = GetPropertyPointer(componentIndex, propertyPath);

        var getResult = entity.EntityData.GetPropertyAsJsonNode(propertyPointer);
        if (getResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get entity property '{propertyPath}' for resource '{resource}'")
                .WithErrors(getResult);
        }

        var jsonNode = getResult.Value;
        var jsonString = jsonNode.ToJsonString();

        return Result<string>.Ok(jsonString);
    }

    public Result SetProperty<T>(ResourceKey resource, int componentIndex, string propertyPath, T newValue, bool insert) where T : notnull
    {
        var acquireResult = _entityRegistry.AcquireEntity(resource);
        if (acquireResult.IsFailure)
        {
            return Result.Fail($"Failed to acquire entity: {resource}")
                .WithErrors(acquireResult);
        }
        var entity = acquireResult.Value;
        Guard.IsNotNull(entity);

        // Construct a patch operation to set the property

        var propertyPointer = GetPropertyPointer(componentIndex, propertyPath);
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
            return Result.Fail($"Failed to apply entity patch for resource: {resource}");
        }

        return Result.Ok();
    }

    public Result SetProperty<T>(ResourceKey resource, string componentType, string propertyPath, T newValue, bool insert) where T : notnull
    {
        var getComponentsResult = GetComponentsOfType(resource, componentType);
        if (getComponentsResult.IsFailure)
        {
            return getComponentsResult;
        }
        var componentIndices = getComponentsResult.Value;

        if (componentIndices.Count < 1)
        {
            return Result.Fail($"No components of type '{componentType}' were found");
        }
        var componentIndex = componentIndices[0];

        return SetProperty(resource, componentIndex, propertyPath, newValue, insert);
    }

    public Result<bool> UndoEntity(ResourceKey resource)
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
            // Undo stack is empty. Succeed but return false to indicate that no changes were undone.
            return Result<bool>.Ok(false);
        }

        // Pop the next patch summary from the Undo stack and apply it to the entity
        var patchSummary = entity.UndoStack.Pop();
        var reversePatchOperation = patchSummary.ReverseOperation;
        var undoGroup = patchSummary.UndoGroup;
        Guard.IsNotNull(reversePatchOperation);

        var applyResult = ApplyPatchOperation(entity, reversePatchOperation, undoGroup, PatchContext.Undo);
        if (applyResult.IsFailure)
        {
            return Result<bool>.Fail($"Failed to apply undo patch to resource: {resource}")
                .WithErrors(applyResult);
        }

        // If the next patch summary in the Undo stack has the same UndoGroup, then undo it as well.
        // The easiest way to do this is to call UndoEntity recursively.
        if (patchSummary.UndoGroup != 0 &&
            entity.UndoStack.Count != 0 &&
            entity.UndoStack.Peek().UndoGroup == undoGroup)
        {
            return UndoEntity(resource);
        }

        // Succeed and return true to indicate that a patch was undone.
        return Result<bool>.Ok(true);
    }

    public Result<bool> RedoEntity(ResourceKey resource)
    {
        var acquireResult = _entityRegistry.AcquireEntity(resource);
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
        var reverseOperation = patchSummary.ReverseOperation;
        var undoGroup = patchSummary.UndoGroup;
        Guard.IsNotNull(reverseOperation);

        var applyResult = ApplyPatchOperation(entity, reverseOperation, undoGroup, PatchContext.Redo);
        if (applyResult.IsFailure)
        {
            return Result<bool>.Fail($"Failed to apply redo patch to resource: {resource}");
        }

        // If the next patch summary in the Redo stack has the same UndoGroup, then redo it as well.
        // The easiest way to do this is to call RedoEntity recursively.
        if (patchSummary.UndoGroup != 0 &&
            entity.RedoStack.Count != 0 &&
            entity.RedoStack.Peek().UndoGroup == undoGroup)
        {
            return RedoEntity(resource);
        }

        // Succeed and return true to indicate that a patch was undone.
        return Result<bool>.Ok(true);
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

    private Result ApplyPatchOperation(Entity entity, PatchOperation patchOperation, long undoGroup, PatchContext context = PatchContext.Modify)
    {
        if (patchOperation.Op == OperationType.Unknown ||
            patchOperation.Op == OperationType.Copy ||
            patchOperation.Op == OperationType.Move ||
            patchOperation.Op == OperationType.Test)
        {
            return Result.Fail($"Patch operation is not supported: {patchOperation.Op}");
        }

        var applyResult = entity.ApplyPatchOperation(patchOperation, _schemaRegistry, undoGroup, context);
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
            }

            _disposed = true;
        }
    }

    ~EntityService()
    {
        Dispose(false);
    }
}
