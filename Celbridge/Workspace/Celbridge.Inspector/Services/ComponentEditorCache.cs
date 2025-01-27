using Celbridge.Entities;
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
    }

    public Result<IComponentEditor> AcquireComponentEditor(ComponentKey componentKey)
    {
        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        if (componentKey.Resource != _inspectedResource)
        {
            return Result<IComponentEditor>.Fail($"Failed to acquire component editor for component: '{componentKey}'. Resource is not being inspected.");
        }

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

        if (_inspectedResource != resource)
        {
            _inspectedResource = resource;

            // Invalidate because a different resource is now being inspected
            InvalidateCache();
        }
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        var component = message.ComponentKey;
        var propertyPath = message.PropertyPath;

        if (_inspectedResource == component.Resource && 
            propertyPath == "/")
        {
            // Invalidate because the entity structure has changed
            InvalidateCache();
        }
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
