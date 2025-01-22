using Celbridge.Entities.Services;
using Json.More;
using Json.Pointer;
using Json.Schema;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Celbridge.Entities.Models;

public class ComponentConfig
{
    private const string ComponentTypeConstKey = "/properties/_componentType/const";
    private const string AttributesKey = "attributes";
    private const string PropertiesKey = "properties";
    private const string PrototypeKey = "prototype";
    private const string FormKey = "form";
    private const string TypeKey = "type";

    public string ComponentType { get; }
    public int ComponentVersion { get; }
    public ComponentSchema ComponentSchema { get; }
    public JsonElement Prototype { get; }
    public Type ComponentEditorType { get; }

    private readonly JsonSchema _jsonSchema;

    private ComponentConfig(
        string componentType, 
        int componentVersion, 
        ComponentSchema componentSchema, 
        JsonElement prototype,
        JsonSchema jsonSchema,
        Type componentEditorType)
    {
        ComponentType = componentType;
        ComponentVersion = componentVersion;
        ComponentSchema = componentSchema;
        Prototype = prototype;
        ComponentEditorType = componentEditorType;
        _jsonSchema = jsonSchema;
    }

    public static Result<ComponentConfig> CreateConfig(Type componentEditorType, string schemaJson)
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

            var componentTypePointer = JsonPointer.Parse(ComponentTypeConstKey);
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

            if (root.TryGetProperty(AttributesKey, out JsonElement attributesElement))
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

            // Get the component properties

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

            var formJson = string.Empty;
            if (root.TryGetProperty(FormKey, out JsonElement formElement))
            { 
                if (formElement.ValueKind != JsonValueKind.Array)
                {
                    return Result<ComponentConfig>.Fail("Form json does not contain an array of form elements");
                }
                formJson = formElement.ToJsonString();
            }            

            var componentSchema = new ComponentSchema(componentType, componentVersion, componentTags, componentAttributes, componentProperties, formJson);

            // Construct the prototype element

            var prototypeNode = root.GetProperty(PrototypeKey).AsNode();
            if (prototypeNode is null)
            {
                return Result<ComponentConfig>.Fail("Prototype node not found");
            }
            prototypeNode["_componentType"] = typeAndVersion; // Prototype type and version match the schema

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

    public Result ValidateJsonObject(JsonObject jsonObject)
    {
        var validationResult = _jsonSchema.Evaluate(jsonObject);
        return validationResult.IsValid
            ? Result.Ok()
            : Result.Fail($"Schema validation failed for component type: '{ComponentType}'");
    }
}
