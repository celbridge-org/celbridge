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
}
