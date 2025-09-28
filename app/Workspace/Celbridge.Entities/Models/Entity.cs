using Celbridge.Entities.Services;
using Json.Patch;

namespace Celbridge.Entities.Models;

public class Entity
{
    public ResourceKey Resource { get; private set; }
    public string EntityDataPath { get; private set; } = string.Empty;
    
    private EntityData? _entityData;
    public EntityData EntityData => _entityData!;

    public Stack<PatchSummary> UndoStack { get; private set; } = new();
    public Stack<PatchSummary> RedoStack { get; private set; } = new();

    public static Entity CreateEntity(ResourceKey resource, string entityDataPath, EntityData entityData)
    {
        var entity = new Entity();
        entity._entityData = entityData;
        entity.SetResourceKey(resource, entityDataPath);
        return entity;
    }

    public void SetResourceKey(ResourceKey resource, string entityDataPath)
    {
        Resource = resource;
        EntityDataPath = entityDataPath;
    }

    public Result<PatchSummary> ApplyPatchOperation(PatchOperation patchOperation, ComponentConfigRegistry configRegistry, long undoGroupId, PatchContext context = PatchContext.Modify)
    {
        var applyResult = EntityData.ApplyPatchOperation(Resource, patchOperation, configRegistry, undoGroupId);
        if (applyResult.IsFailure)
        {
            return Result<PatchSummary>.Fail($"Failed to apply component patch to for resource: '{Resource}'")
                .WithErrors(applyResult);
        }
        var patchSummary = applyResult.Value;

        if (patchSummary.ComponentChangedMessage is null)
        {
            // Patch applied successfully but no component changes were made.
            return Result<PatchSummary>.Ok(patchSummary);
        }

        // Add the patch summary to the requested stack to support undo/redo
        switch (context)
        {
            case PatchContext.Modify:
                // Execute: Add patch summary to the Undo stack and clear the Redo stack.
                UndoStack.Push(patchSummary);
                RedoStack.Clear();
                break;
            case PatchContext.Undo:
                // Undo: Add patch summary to the Redo stack.
                RedoStack.Push(patchSummary);
                break;
            case PatchContext.Redo:
                // Undo: Add patch summary to the Undo stack.
                UndoStack.Push(patchSummary);
                break;
        }

        return Result<PatchSummary>.Ok(patchSummary);
    }
}
