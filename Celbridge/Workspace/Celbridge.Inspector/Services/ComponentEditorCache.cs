using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Inspector.Services;

public class ComponentEditorCache
{
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private Dictionary<ComponentKey, IComponentEditor> _editorCache = new();

    private ResourceKey _inspectedResource;

    public ComponentEditorCache(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;

        _messengerService.Register<InspectedComponentChangedMessage>(this, OnInspectedComponentChangedMessage);
        _messengerService.Register<ComponentChangedMessage>(this, OnComponentChangedMessage);
        _messengerService.Register<ResourceRegistryUpdatedMessage>(this, OnResourceRegistryUpdatedMessage);
    }

    public Result<IComponentEditor> AcquireComponentEditor(ComponentKey componentKey)
    {
        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        // Clients may acquire a component editor for any resource, not just the inspected resource.
        // The cache is cleared on any structural change to entities or the inspected resource. Clients should not
        // rely on an editor instance being valid beyond the scope of the current operation.

        if (_editorCache.TryGetValue(componentKey, out var cachedEditor))
        {
            return Result<IComponentEditor>.Ok(cachedEditor);
        }

        var createEditorResult = entityService.CreateComponentEditor(componentKey);
        if (createEditorResult.IsFailure)
        {
            return Result<IComponentEditor>.Fail($"Failed to create component editor for component: '{componentKey}'")
                .WithErrors(createEditorResult);
        }
        var editor = createEditorResult.Value;

        _editorCache.Add(componentKey, editor);

        return Result<IComponentEditor>.Ok(editor);
    }

    private void OnInspectedComponentChangedMessage(object recipient, InspectedComponentChangedMessage message)
    {
        var resource = message.ComponentKey.Resource;

        // Invalidate the cache when a different resource is inspected
        if (_inspectedResource != resource)
        {
            _inspectedResource = resource;
            InvalidateCache();
        }
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        var component = message.ComponentKey;
        var propertyPath = message.PropertyPath;

        // Invalidate the cache if the entity component structure of any cached editor changes.
        foreach (var kv in _editorCache)
        {
            var componentKey = kv.Key;
            if (component.Resource == componentKey.Resource &&
                propertyPath == "/")
            {
                _editorCache.Remove(componentKey);
                InvalidateCache();
                break;
            }
        }
    }

    private void OnResourceRegistryUpdatedMessage(object recipient, ResourceRegistryUpdatedMessage message)
    {
        // Invalidate the cache whenever the resource registry updates
        InvalidateCache();
    }

    private void InvalidateCache()
    {
        if (_editorCache.Count == 0)
        {
            // No cached editors to invalidate
            return;
        }

        _editorCache.Clear();

        var message = new ComponentEditorCacheInvalidated();
        _messengerService.Send(message);
    }
}
