using Celbridge.Entities.Models;

namespace Celbridge.Entities.Services;

public class ComponentConfigRegistry
{
    private readonly IServiceProvider _serviceProvider;

    private readonly Dictionary<string, ComponentConfig> _componentConfigs = new();
    public IReadOnlyDictionary<string, ComponentConfig> ComponentConfigs => _componentConfigs;

    public ComponentConfigRegistry(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Result Initialize()
    {
        try
        {
            var descriptorTypes = GetDescriptorTypes();

            foreach (var kv in descriptorTypes)
            {
                var descriptorKey = kv.Key;
                var objectType = kv.Value;

                var descriptor = _serviceProvider.GetRequiredService(objectType) as IComponentDescriptor;
                if (descriptor is null)
                {
                    return Result.Fail($"Failed to instantiate component descriptor: '{descriptorKey}'");
                }

                // Create the component config

                var schemaJson = descriptor.SchemaJson;
                var createResult = ComponentConfig.CreateConfig(descriptor, schemaJson);
                if (!createResult.IsSuccess)
                {
                    return Result.Fail($"Failed to create component config from JSON for component descriptor: '{descriptorKey}'")
                        .WithErrors(createResult);
                }
                var config = createResult.Value;

                // Perform checks

                if (!descriptorKey.EndsWith("Component"))
                {
                    return Result.Fail($"Component descriptor name does not end with 'Component': '{descriptorKey}'");
                }

                // Remove trailing "Component" from descriptor key to form the componentType
                var componentType = descriptorKey[..^9];

                if (componentType != config.ComponentType)
                {
                    return Result.Fail($"Component type does not match type defined in schema: '{descriptorKey}'");
                }

                if (_componentConfigs.ContainsKey(componentType))
                {
                    return Result.Fail($"Component config already exists: '{componentType}'");
                }

                // Register the config

                _componentConfigs[componentType] = config;
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when loading schema JSON files")
                .WithException(ex);
        }
    }

    public Result<ComponentConfig> GetComponentConfig(string componentType)
    {
        if (!_componentConfigs.TryGetValue(componentType, out var config))
        {
            return Result<ComponentConfig>.Fail($"Component config not found: '{componentType}'");
        }

        return Result<ComponentConfig>.Ok(config);
    }

    private Dictionary<string, Type> GetDescriptorTypes()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        var descriptorTypes = new Dictionary<string, Type>();
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (typeof(IComponentDescriptor).IsAssignableFrom(type) && !type.IsAbstract && type.IsClass)
                {
                    descriptorTypes[type.Name] = type;
                }
            }
        }

        return descriptorTypes;
    }
}
