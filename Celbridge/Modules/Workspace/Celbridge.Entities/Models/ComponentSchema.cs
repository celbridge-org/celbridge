using Json.More;
using Json.Pointer;
using Json.Schema;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Celbridge.Entities.Models;

public class ComponentSchema
{
    private const string ComponentTypeConstKey = "/properties/_componentType/const";
    private const string ComponentVersionConstKey = "/properties/_componentVersion/const";
    private const string AttributesKey = "attributes";
    private const string PropertiesKey = "properties";
    private const string TypeKey = "type";

    public string ComponentType { get; }
    public int ComponentVersion { get; }
    public ComponentInfo ComponentInfo { get; }

    private readonly JsonSchema _jsonSchema;

    private ComponentSchema(string componentType, int componentVersion, ComponentInfo componentInfo, JsonSchema jsonSchema)
    {
        ComponentType = componentType;
        ComponentVersion = componentVersion;
        ComponentInfo = componentInfo;
        _jsonSchema = jsonSchema;
    }

    public static Result<ComponentSchema> FromJson(string schemaJson)
    {
        try
        {
            using var document = JsonDocument.Parse(schemaJson);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result<ComponentSchema>.Fail("Failed to parse schema JSON as an object");
            }

            // Get component type

            var componentTypePointer = JsonPointer.Parse(ComponentTypeConstKey);
            var componentTypeElement = componentTypePointer.Evaluate(root);

            if (componentTypeElement is null ||
                componentTypeElement.Value.ValueKind != JsonValueKind.String)
            {
                return Result<ComponentSchema>.Fail("Component type element is not valid");
            }

            var componentType = componentTypeElement.Value.GetString();
            if (string.IsNullOrEmpty(componentType))
            {
                return Result<ComponentSchema>.Fail("Component type is empty");
            }

            // Get component version 
            
            var componentVersionPointer = JsonPointer.Parse(ComponentVersionConstKey);
            var componentVersionElement = componentVersionPointer.Evaluate(root);

            if (componentVersionElement is null ||
                componentVersionElement.Value.ValueKind != JsonValueKind.Number)
            {
                return Result<ComponentSchema>.Fail("Component version element is not valid");
            }
            var componentVersion = componentVersionElement.Value.GetInt32();

            // Populate the component info

            var componentAttributes = new Dictionary<string, string>();
            if (root.TryGetProperty(AttributesKey, out JsonElement attributesElement))
            {
                foreach (var attribute in attributesElement.EnumerateObject())
                {
                    componentAttributes[attribute.Name] = attribute.Value.ToJsonString();
                }
            }

            var componentProperties = new List<ComponentPropertyInfo>();
            if (root.TryGetProperty(PropertiesKey, out JsonElement propertiesElement))
            {
                foreach (var propertyElement in propertiesElement.EnumerateObject())
                {
                    var propertyName = propertyElement.Name;
                    if (propertyName.StartsWith('_'))
                    {
                        // Ignore internal-only properties
                        continue;
                    }

                    var propertyType = propertyElement.Value.GetProperty(TypeKey).ToString();
                    var propertyAttributes = new Dictionary<string, string>();
                    if (propertyElement.Value.TryGetProperty(AttributesKey, out JsonElement propertyAttributesElement))
                    {
                        foreach (var attribute in propertyAttributesElement.EnumerateObject())
                        {
                            propertyAttributes[attribute.Name] = attribute.Value.ToString();
                        }
                    }

                    var propertyInfo = new ComponentPropertyInfo(propertyName, propertyType, propertyAttributes);
                    componentProperties.Add(propertyInfo);
                }
            }

            var componentInfo = new ComponentInfo(componentType, componentAttributes, componentProperties);

            // Create the JsonSchema object

            var jsonSchema = JsonSchema.FromText(schemaJson);
            if (jsonSchema is null)
            {
                return Result<ComponentSchema>.Fail($"Failed to parse schema for component type: '{componentType}'");
            }

            var schema = new ComponentSchema(componentType, componentVersion, componentInfo, jsonSchema);

            return Result<ComponentSchema>.Ok(schema);
        }
        catch (Exception ex)
        {
            return Result<ComponentSchema>.Fail("An exception occurred when parsing schema JSON.")
                .WithException(ex);
        }
    }

    public Result ValidateJsonObject(JsonObject jsonObject)
    {
        var validationResult = _jsonSchema.Evaluate(jsonObject);
        return validationResult.IsValid
            ? Result.Ok()
            : Result.Fail($"Validation failed with schema '{ComponentType}'");
    }

    public Result ValidateJson(string json)
    {
        try
        {
            var jsonObject = JsonNode.Parse(json) as JsonObject;
            if (jsonObject is null)
            {
                return Result.Fail("Failed to parse JSON data as a JSON object");
            }

            return ValidateJsonObject(jsonObject);
        }
        catch (Exception ex)
        {
            return Result.Fail("An exception occurred when validating JSON data.")
                .WithException(ex);
        }
    }
}
