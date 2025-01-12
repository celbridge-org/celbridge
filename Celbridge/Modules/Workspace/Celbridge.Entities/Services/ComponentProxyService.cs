using Celbridge.Entities.Models;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;

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
        var resource = message.Resource;
        var propertyPath = message.PropertyPath;

        if (propertyPath == "/")
        {
            if (_componentCache.TryGetValue(resource, out var indexCache))
            {
                // Invalidate all proxies in the index cache before removing them.
                // If a client is holding a reference to a proxy, they can check if it is valid before using it.
                foreach (var proxy in indexCache.Values)
                {
                    proxy.IsValid = false;
                }
            }
            _componentCache.Remove(resource);
            _componentListCache.Remove(resource);
        }
    }

    public Result<IComponentProxy> GetComponent(ResourceKey resource, int componentIndex)
    {
        Guard.IsNotNull(_entityService);

        // Attempt to get the proxy from the cache
        if (!_componentCache.TryGetValue(resource, out var indexCache))
        {
            indexCache = new Dictionary<int, ComponentProxy>();
            _componentCache[resource] = indexCache;
        }
        if (indexCache.TryGetValue(componentIndex, out var cachedProxy))
        {
            return Result<IComponentProxy>.Ok(cachedProxy);
        }

        // No proxy found in the cache, create a new one

        // Get the component type
        var getTypeResult = _entityService.GetComponentType(resource, componentIndex);
        if (getTypeResult.IsFailure)
        {
            return Result<IComponentProxy>.Fail($"Failed to get component type for resource '{resource}' at component index {componentIndex}")
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

        var proxy = new ComponentProxy(_serviceProvider, resource, componentIndex, schema);
        indexCache[componentIndex] = proxy;

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
                var getComponentResult = GetComponent(resource, i);
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
