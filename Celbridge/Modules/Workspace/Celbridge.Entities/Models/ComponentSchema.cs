using Json.Pointer;
using Json.Schema;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Celbridge.Entities.Models;

public class ComponentSchema
{
    public string ComponentType { get; }
    public int ComponentVersion { get; }

    private readonly JsonSchema _jsonSchema;

    private ComponentSchema(string componentType, int componentVersion, JsonSchema jsonSchema)
    {
        ComponentType = componentType;
        ComponentVersion = componentVersion;
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
            var componentTypePointer = JsonPointer.Parse("/properties/_componentType/const");
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
            var componentVersionPointer = JsonPointer.Parse("/properties/_componentVersion/const");
            var componentVersionElement = componentVersionPointer.Evaluate(root);

            if (componentVersionElement is null ||
                componentVersionElement.Value.ValueKind != JsonValueKind.Number)
            {
                return Result<ComponentSchema>.Fail("Component version element is not valid");
            }

            var componentVersion = componentVersionElement.Value.GetInt32();

            // Create the JsonSchema object
            var jsonSchema = JsonSchema.FromText(schemaJson);
            if (jsonSchema is null)
            {
                return Result<ComponentSchema>.Fail($"Failed to parse schema for component type: '{componentType}'");
            }

            var schema = new ComponentSchema(componentType, componentVersion, jsonSchema);

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
