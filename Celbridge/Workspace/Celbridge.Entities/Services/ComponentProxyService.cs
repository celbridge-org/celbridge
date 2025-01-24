using Celbridge.Entities.Models;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Entities.Services;

public class ComponentProxyService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private IEntityService? _entityService;

    private Dictionary<ResourceKey, Dictionary<int, ComponentProxy>> _componentCache = new();
    private Dictionary<ResourceKey, IReadOnlyList<IComponentProxy>> _componentListCache = new();

    public ComponentProxyService(
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _serviceProvider = serviceProvider;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
    }

    public Result Initialize()
    {
        _entityService = _workspaceWrapper.WorkspaceService.EntityService;

        _messengerService.Register<ComponentChangedMessage>(this, OnComponentChangedMessage);

        return Result.Ok();
    }

    public Result Uninitialize()
    {
        _messengerService.UnregisterAll(this);

        return Result.Ok();
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        var resource = message.ComponentKey.Resource;
        var propertyPath = message.PropertyPath;

        if (propertyPath == "/")
        {
            // Remove the proxy from caches.
            if (_componentCache.TryGetValue(resource, out var indexCache))
            {
                // Invalidate all proxies in the index cache before clearing the cache.
                foreach (var proxy in indexCache.Values)
                {
                    // Just to be safe, the proxy also listens for the same event and invalidates itself.
                    proxy.Invalidate();
                }
            }

            _componentCache.Remove(resource);
            _componentListCache.Remove(resource);
        }
    }

    public Result<IComponentProxy> GetComponent(ComponentKey componentKey)
    {
        Guard.IsNotNull(_entityService);

        // Attempt to get the proxy from the cache
        if (!_componentCache.TryGetValue(componentKey.Resource, out var indexCache))
        {
            indexCache = new Dictionary<int, ComponentProxy>();
            _componentCache[componentKey.Resource] = indexCache;
        }
        if (indexCache.TryGetValue(componentKey.ComponentIndex, out var cachedProxy))
        {
            return Result<IComponentProxy>.Ok(cachedProxy);
        }

        // No proxy found in the cache, create a new one

        // Get the component type
        var getTypeResult = _entityService.GetComponentType(componentKey);
        if (getTypeResult.IsFailure)
        {
            return Result<IComponentProxy>.Fail($"Failed to get component type: '{componentKey}'")
                .WithErrors(getTypeResult);
        }
        var componentType = getTypeResult.Value;

        // Get the component schema
        var getSchemaResult = _entityService.GetComponentSchema(componentType);
        if (getSchemaResult.IsFailure)
        {
            return Result<IComponentProxy>.Fail($"Failed to get component schema for component type '{componentType}'")
            .WithErrors(getSchemaResult);
        }
        var schema = getSchemaResult.Value;

        var proxy = new ComponentProxy(_serviceProvider, componentKey, schema);
        indexCache[componentKey.ComponentIndex] = proxy;

        return Result<IComponentProxy>.Ok(proxy);
    }

    public Result<IComponentProxy> GetComponentOfType(ResourceKey resource, string componentType)
    {
        if (string.IsNullOrEmpty(componentType))
        {
            return Result<IComponentProxy>.Fail("Component type must be specified");
        }

        var getComponentsResult = GetComponents(resource, componentType);
        if (getComponentsResult.IsFailure)
        {
            return Result<IComponentProxy>.Fail($"Failed to get components for resource '{resource}'")
                .WithErrors(getComponentsResult);
        }
        var components = getComponentsResult.Value;

        if (components.Count == 0)
        {
            return Result<IComponentProxy>.Fail($"No components of type '{componentType}' found for resource '{resource}'");
        }

        return Result<IComponentProxy>.Ok(components[0]);
    }

    public Result<IReadOnlyList<IComponentProxy>> GetComponents(ResourceKey resource, string componentType)
    {
        Guard.IsNotNull(_entityService);

        // Attempt to get the component list from the cache
        if (!_componentListCache.TryGetValue(resource, out var componentList))
        {
            // List was not previously cached, populate the component list now

            var componentCount = _entityService.GetComponentCount(resource);
            var newList = new List<IComponentProxy>(componentCount);

            for (int i = 0; i < componentCount; i++)
            {
                var getComponentResult = GetComponent(new ComponentKey(resource, i));
                if (getComponentResult.IsFailure)
                {
                    return Result<IReadOnlyList<IComponentProxy>>.Fail($"Failed to get component for resource '{resource}' at index {i}")
                        .WithErrors(getComponentResult);
                }
                var component = getComponentResult.Value;

                newList.Add(component);
            }

            _componentListCache[resource] = newList;
        }

        var components = _componentListCache[resource];

        if (string.IsNullOrEmpty(componentType))
        {
            // Return the full list
            return Result<IReadOnlyList<IComponentProxy>>.Ok(components);
        }

        // Return only the components of the specified type
        var filtered = components.Where(c => c.Schema.ComponentType == componentType).ToList();
        return Result<IReadOnlyList<IComponentProxy>>.Ok(filtered);
    }
}
