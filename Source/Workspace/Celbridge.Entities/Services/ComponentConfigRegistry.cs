using Celbridge.Entities.Models;
using Celbridge.Forms;
using Json.More;
using Json.Pointer;
using Json.Schema;
using System.Text.Json;

namespace Celbridge.Entities.Services;

public class ComponentConfigRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IFormService _formService;

    private readonly Dictionary<string, ComponentConfig> _componentConfigs = new();
    public IReadOnlyDictionary<string, ComponentConfig> ComponentConfigs => _componentConfigs;

    public ComponentConfigRegistry(
        IServiceProvider serviceProvider,
        IFormService formService)
    {
        _serviceProvider = serviceProvider;
        _formService = formService;
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

                //
                // Create the component config
                //

                // Load the component config JSON from the config editor
                var loadConfigResult = LoadComponentConfig(editorType);
                if (loadConfigResult.IsFailure)
                {
                    return Result.Fail($"Failed to load component config for component editor: '{editorKey}'")
                        .WithErrors(loadConfigResult);
                }
                var configJson = loadConfigResult.Value;

                // Create the component config
                var createResult = CreateComponentConfig(editorType, configJson);
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

    private Result<string> LoadComponentConfig(Type componentEditorType)
    {
        var editor = _serviceProvider.GetRequiredService(componentEditorType) as IComponentEditor;
        if (editor is null)
        {
            return Result<string>.Fail($"Failed to instantiate component editor type: '{componentEditorType}'");
        }

        string config = editor.GetComponentConfig();
        if (string.IsNullOrEmpty(config))
        {
            return Result<string>.Fail($"Failed to get component config for component editor: '{componentEditorType}'");
        }

        return Result<string>.Ok(config);
    }

    private Result<ComponentConfig> CreateComponentConfig(Type componentEditorType, string schemaJson)
    {
        try
        {
            using var document = JsonDocument.Parse(schemaJson);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result<ComponentConfig>.Fail("Failed to parse schema JSON as an object");
            }

            // Get component type and version

            var componentTypePointer = JsonPointer.Parse(ComponentConfig.ComponentTypeConstKey);
            var componentTypeElement = componentTypePointer.Evaluate(root);

            if (componentTypeElement is null ||
                componentTypeElement.Value.ValueKind != JsonValueKind.String)
            {
                return Result<ComponentConfig>.Fail("Component type element is not valid");
            }

            var typeAndVersion = componentTypeElement.Value.GetString();
            if (string.IsNullOrEmpty(typeAndVersion))
            {
                return Result<ComponentConfig>.Fail("Component type is empty");
            }

            var parseResult = EntityUtils.ParseComponentTypeAndVersion(typeAndVersion);
            if (parseResult.IsFailure)
            {
                return Result<ComponentConfig>.Fail($"Failed to parse component type and version: {typeAndVersion}")
                    .WithErrors(parseResult);
            }
            var (componentType, componentVersion) = parseResult.Value;

            // Get the component attributes

            var componentTags = new HashSet<string>();
            var componentAttributes = new Dictionary<string, string>();

            if (root.TryGetProperty(ComponentConfig.AttributesKey, out JsonElement attributesElement))
            {
                foreach (var attribute in attributesElement.EnumerateObject())
                {
                    if (attribute.Name == "tags")
                    {
                        // Tags are treated specially to support fast querying

                        foreach (var tag in attribute.Value.EnumerateArray())
                        {
                            if (tag.ValueKind != JsonValueKind.String)
                            {
                                return Result<ComponentConfig>.Fail("Tag value is not a string");
                            }

                            componentTags.Add(tag.ToString());
                        }
                    }
                    else
                    {
                        componentAttributes[attribute.Name] = attribute.Value.ToString();
                    }
                }
            }

            // Add the component namespace as a tag for convenience
            var dotIndex = componentType.LastIndexOf('.');
            if (dotIndex > 0)
            {
                var componentNamespace = componentType.Substring(0, dotIndex);
                if (!string.IsNullOrEmpty(componentNamespace))
                {
                    componentTags.Add(componentNamespace);
                }
            }

            // Get the component properties

            var componentProperties = new List<ComponentPropertyInfo>();
            if (root.TryGetProperty(ComponentConfig.PropertiesKey, out JsonElement propertiesElement))
            {
                foreach (var propertyElement in propertiesElement.EnumerateObject())
                {
                    var propertyName = propertyElement.Name;
                    if (propertyName.StartsWith('_'))
                    {
                        // Ignore internal-only properties
                        continue;
                    }

                    var propertyType = propertyElement.Value.GetProperty(ComponentConfig.TypeKey).ToString();
                    var propertyAttributes = new Dictionary<string, string>();
                    if (propertyElement.Value.TryGetProperty(ComponentConfig.AttributesKey, out JsonElement propertyAttributesElement))
                    {
                        foreach (var attribute in propertyAttributesElement.EnumerateObject())
                        {
                            propertyAttributes[attribute.Name] = attribute.Value.ToString();
                        }
                    }

                    // If the schema defines an enum list then store it as a Json string attribute.
                    if (propertyElement.Value.TryGetProperty(ComponentConfig.EnumKey, out var enumValue))
                    {
                        propertyAttributes[ComponentConfig.EnumKey] = enumValue.ToJsonString();
                    }

                    var propertyInfo = new ComponentPropertyInfo(propertyName, propertyType, propertyAttributes);
                    componentProperties.Add(propertyInfo);
                }
            }

            // Build the component schema

            var componentSchema = new ComponentSchema(componentType, componentVersion, componentTags, componentAttributes, componentProperties);

            // Construct the prototype element

            var prototypeNode = root.GetProperty(ComponentConfig.PrototypeKey).AsNode();
            if (prototypeNode is null)
            {
                return Result<ComponentConfig>.Fail("Prototype node not found");
            }
            prototypeNode[EntityUtils.ComponentTypeKey] = typeAndVersion; // Prototype type and version match the schema

            var prototype = JsonSerializer.Deserialize<JsonElement>(prototypeNode.ToJsonString());

            // Create the JsonSchema object

            var jsonSchema = JsonSchema.FromText(schemaJson);
            if (jsonSchema is null)
            {
                return Result<ComponentConfig>.Fail($"Failed to parse JSON schema for component type: '{componentType}'");
            }

            // Validate the prototype

            var evaluateResult = jsonSchema.Evaluate(prototype);
            if (!evaluateResult.IsValid)
            {
                return Result<ComponentConfig>.Fail($"Prototype failed schema validation: '{componentType}'");
            }

            var config = new ComponentConfig(componentType, componentVersion, componentSchema, prototype, jsonSchema, componentEditorType);

            return Result<ComponentConfig>.Ok(config);
        }
        catch (Exception ex)
        {
            return Result<ComponentConfig>.Fail("An exception occurred when creating component config.")
                .WithException(ex);
        }
    }
}
