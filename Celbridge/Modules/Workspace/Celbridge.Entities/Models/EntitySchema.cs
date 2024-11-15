using Json.Pointer;
using Json.Schema;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Celbridge.Entities.Models;

public class EntitySchema
{
    public string EntityType { get; }
    public int EntityVersion { get; }

    private readonly JsonSchema _jsonSchema;

    private EntitySchema(string entityType, int entityVersion, JsonSchema jsonSchema)
    {
        EntityType = entityType;
        EntityVersion = entityVersion;
        _jsonSchema = jsonSchema;
    }

    public static Result<EntitySchema> FromJson(string schemaJson)
    {
        try
        {
            using var document = JsonDocument.Parse(schemaJson);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result<EntitySchema>.Fail("Failed to parse schema JSON as an object");
            }

            // Get entity type
            var entityTypePointer = JsonPointer.Parse("/properties/_entityType/const");
            var entityTypeElement = entityTypePointer.Evaluate(root);

            if (entityTypeElement is null ||
                entityTypeElement.Value.ValueKind != JsonValueKind.String)
            {
                return Result<EntitySchema>.Fail("Entity type element is not valid");
            }

            var entityType = entityTypeElement.Value.GetString();
            if (string.IsNullOrEmpty(entityType))
            {
                return Result<EntitySchema>.Fail("Entity type is empty");
            }

            // Get entity version 
            var entityVersionPointer = JsonPointer.Parse("/properties/_entityVersion/const");
            var entityVersionElement = entityVersionPointer.Evaluate(root);

            if (entityVersionElement is null ||
                entityVersionElement.Value.ValueKind != JsonValueKind.Number)
            {
                return Result<EntitySchema>.Fail("Entity version element is not valid");
            }

            var entityVersion = entityVersionElement.Value.GetInt32();

            // Create the JsonSchema object
            var jsonSchema = JsonSchema.FromText(schemaJson);
            if (jsonSchema is null)
            {
                return Result<EntitySchema>.Fail($"Failed to parse schema for entity type: '{entityType}'");
            }

            return Result<EntitySchema>.Ok(new EntitySchema(entityType, entityVersion, jsonSchema));
        }
        catch (Exception ex)
        {
            return Result<EntitySchema>.Fail("An exception occurred when parsing schema JSON.")
                .WithException(ex);
        }
    }

    public Result ValidateJsonObject(JsonObject jsonObject)
    {
        var validationResult = _jsonSchema.Evaluate(jsonObject);
        return validationResult.IsValid
            ? Result.Ok()
            : Result.Fail($"Validation failed with schema '{EntityType}'");
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
