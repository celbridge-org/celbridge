using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Inspector.Services;

public record UpdatedAnnotationCache();

public class EntityAnnotationCache
{
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private readonly Dictionary<ResourceKey, IEntityAnnotation> _cachedAnnotations = new();

    public EntityAnnotationCache(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;

        _messengerService.Register<AnnotatedEntityMessage>(this, OnAnnotatedEntityMessage);
        _messengerService.Register<ResourceRegistryUpdatedMessage>(this, OnResourceRegistryUpdatedMessage);
    }

    public Result<IEntityAnnotation> GetEntityAnnotation(ResourceKey resource)
    {
        if (_cachedAnnotations.TryGetValue(resource, out var annotation))
        {
            return Result<IEntityAnnotation>.Ok(annotation);
        }

        return Result<IEntityAnnotation>.Fail();
    }

    private void OnAnnotatedEntityMessage(object recipient, AnnotatedEntityMessage message)
    {
        var resource = message.Resource;
        var annotation = message.EntityAnnotation;

        _cachedAnnotations[resource] = annotation;

        var updatedMessage = new UpdatedAnnotationCache();
        _messengerService.Send(updatedMessage);
    }

    private void OnResourceRegistryUpdatedMessage(object recipient, ResourceRegistryUpdatedMessage message)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        // Remove cached entries for resources that no longer exist.
        bool updated = false;
        foreach (var kv in _cachedAnnotations)
        {
            var resource = kv.Key;

            var getResult = resourceRegistry.GetResource(resource);
            if (getResult.IsFailure)
            {
                _cachedAnnotations.Remove(resource);
                updated = true;
            }
        }

        if (updated)
        {
            var updatedMessage = new UpdatedAnnotationCache();
            _messengerService.Send(updatedMessage);
        }
    }
}
