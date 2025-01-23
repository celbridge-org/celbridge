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
            var getTypesResult = GetComponentEditorTypes();
            if (getTypesResult.IsFailure)
            {
                return Result.Fail($"Failed to get component editor types")
                    .WithErrors(getTypesResult);
            }
            var editorTypes = getTypesResult.Value;

            foreach (var editorType in editorTypes)
            {
                var editorKey = editorType.Name;

                // Create the component config

                // Load the component config JSON from an embedded resource
                var loadConfigResult = LoadComponentConfigFile(editorType);
                if (loadConfigResult.IsFailure)
                {
                    return Result.Fail($"Failed to load component config for component editor: '{editorKey}'")
                        .WithErrors(loadConfigResult);
                }
                var configJson = loadConfigResult.Value;

                // Create the component config
                var createResult = ComponentConfig.CreateConfig(editorType, configJson);
                if (!createResult.IsSuccess)
                {
                    return Result.Fail($"Failed to create component config from JSON for component editor: '{editorKey}'")
                        .WithErrors(createResult);
                }
                var config = createResult.Value;

                // Perform checks

                if (!editorKey.EndsWith("Editor"))
                {
                    return Result.Fail($"Component editor name does not end with 'Editor': '{editorKey}'");
                }

                var configComponentType = config.ComponentType;

                // Sanity check that the component type matches the editor type
                // This doesn't account for component namespaces, that would require an attribute on the editor class.
                var editorComponentType = editorKey[..^6];
                if (!configComponentType.EndsWith($".{editorComponentType}"))
                {
                    return Result.Fail($"Component type does not match type defined in schema: '{editorKey}'");
                }

                if (_componentConfigs.ContainsKey(configComponentType))
                {
                    return Result.Fail($"Component config already exists: '{configComponentType}'");
                }

                // Register the config

                _componentConfigs[configComponentType] = config;
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

    private Result<List<Type>> GetComponentEditorTypes()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        var editorTypes = new List<Type>();
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (!typeof(IComponentEditor).IsAssignableFrom(type) || 
                    type.IsAbstract || 
                    !type.IsClass)
                {
                    continue;
                }

                var editor = _serviceProvider.GetRequiredService(type) as IComponentEditor;
                if (editor is null)
                {
                    return Result<List<Type>>.Fail($"Failed to instantiate component editor type: '{type}'");
                }

                editorTypes.Add(type);
            }
        }

        return Result<List<Type>>.Ok(editorTypes);
    }

    private Result<string> LoadComponentConfigFile(Type componentEditorType)
    {
        var editor = _serviceProvider.GetRequiredService(componentEditorType) as IComponentEditor;
        if (editor is null)
        {
            return Result<string>.Fail($"Failed to instantiate component editor type: '{componentEditorType}'");
        }
        string configPath = editor.ComponentConfigPath;

        // Load the component config JSON from an embedded resource
        var assembly = componentEditorType.Assembly;
        var stream = assembly.GetManifestResourceStream(configPath);
        if (stream is null)
        {
            return Result<string>.Fail($"Embedded resource not found: '{configPath}'");
        }

        var json = string.Empty;
        try
        {
            using (stream)
            using (StreamReader reader = new StreamReader(stream))
            {
                json = reader.ReadToEnd();
            }
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"An exception occurred when reading content of embedded resource: {configPath}")
                .WithException(ex);
        }

        return Result<string>.Ok(json);
    }
}
