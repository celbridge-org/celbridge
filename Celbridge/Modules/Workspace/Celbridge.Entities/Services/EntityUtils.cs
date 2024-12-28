using Celbridge.Entities.Models;
using Json.Patch;
using Json.Pointer;
using Json.Schema;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace Celbridge.Entities.Services;

public static class EntityUtils
{
    /// <summary>
    /// Loads and validates an EntityData object from a json file.
    /// </summary>
    public static Result<EntityData> LoadEntityDataFile(string entityDataPath, JsonSchema entitySchema, ComponentSchemaRegistry schemaRegistry)
    {
        // Load the EntityData json
        var jsonObject = JsonNode.Parse(File.ReadAllText(entityDataPath)) as JsonObject;
        if (jsonObject is null)
        {
            return Result<EntityData>.Fail($"Failed to parse entity data from file: '{entityDataPath}'");
        }

        // Todo: Attempt to repair/migrate the data instead of just failing

        // Validate the loaded data against the entity schema
        var evaluateResult = entitySchema.Evaluate(jsonObject);
        if (!evaluateResult.IsValid)
        {
            return Result<EntityData>.Fail($"Entity data failed schema validation: '{entityDataPath}'");
        }

        var checkSchemasResult = CheckComponentSchemas(jsonObject, schemaRegistry);
        if (checkSchemasResult.IsFailure)
        {
            return Result<EntityData>.Fail($"Failed to validate component schemas for entity: '{entityDataPath}'")
                .WithErrors(checkSchemasResult);
        }

        var checkMultipleResult = CheckMultipleComponents(jsonObject, schemaRegistry);
        if (checkMultipleResult.IsFailure)
        {
            return Result<EntityData>.Fail($"Entity data contains invalid multiple components: '{entityDataPath}'")
                .WithErrors(checkMultipleResult);
        }

        // We've passed validation so now we can create and return the EntityData object
        var entityData = EntityData.Create(jsonObject, entitySchema);

        return Result<EntityData>.Ok(entityData);
    }

    /// <summary>
    /// Returns a list of indices of components of the specified type in the entity data.
    /// </summary>
    public static Result<List<int>> GetComponentsOfType(JsonObject entityData, string componentType)
    {
        if (entityData[EntityConstants.ComponentsKey] is not JsonArray components)
        {
            return Result<List<int>>.Fail("Entity data does not contain any components");
        }

        var indices = new List<int>();
        for (int i = 0; i < components.Count; i++)
        {
            var propertyPath = JsonPointer.Create(EntityConstants.ComponentsKey, i, EntityConstants.ComponentTypeKey);
            if (!propertyPath.TryEvaluate(entityData, out var componentTypeNode) ||
                componentTypeNode is null ||
                componentTypeNode.GetValueKind() != JsonValueKind.String)
            {
                continue;
            }

            var typeAndVersion = componentTypeNode.GetValue<string>();

            var parseResult = ParseComponentTypeAndVersion(typeAndVersion);
            if (parseResult.IsFailure)
            {
                return Result<List<int>>.Fail($"Failed to parse component type and version: {typeAndVersion}")
                    .WithErrors(parseResult);
            }
            var (parsedType, _) = parseResult.Value;

            if (parsedType == componentType)
            {
                indices.Add(i);
            }
        }

        return Result<List<int>>.Ok(indices);
    }

    /// <summary>
    /// Checks if the entity data contains multiple components of the same type where the component schema does not allow it.
    /// </summary>
    public static Result CheckMultipleComponents(JsonObject entityData, ComponentSchemaRegistry schemaRegistry)
    {
        if (entityData[EntityConstants.ComponentsKey] is not JsonArray components)
        {
            return Result.Fail($"Entity data does not contain a '{EntityConstants.ComponentsKey}' property.");
        }

        var checkedComponentTypes = new HashSet<string>();

        for (int i = 0; i < components.Count; i++)
        {
            var componentTypePointer = JsonPointer.Parse($"/{EntityConstants.ComponentsKey}/{i}/{EntityConstants.ComponentTypeKey}");

            if (!componentTypePointer.TryEvaluate(entityData, out var componentTypeNode) ||
                componentTypeNode is null ||
                componentTypeNode.GetValueKind() != JsonValueKind.String)
            {
                return Result.Fail($"Failed to get component type for component at index {i}.");
            }

            var typeAndVersion = componentTypeNode.GetValue<string>();

            var parseResult = ParseComponentTypeAndVersion(typeAndVersion);
            if (parseResult.IsFailure)
            {
                return Result.Fail($"Failed to parse component type and version: {typeAndVersion}")
                    .WithErrors(parseResult);
            }
            var (componentType, componentVersion) = parseResult.Value; 

            if (checkedComponentTypes.Contains(componentType))
            {
                // We've already checked this component type
                continue;
            }

            var checkResult = CheckMultipleComponents(entityData, componentType, schemaRegistry);
            if (checkResult.IsFailure)
            {
                return Result.Fail($"Duplicate component check failed for component type '{componentType}'.")
                    .WithErrors(checkResult);
            }

            checkedComponentTypes.Add(componentType);
        }

        return Result.Ok();
    }

    /// <summary>
    /// Checks if the entity data contains multiple instance of any component type where the schema does not allow it.
    /// </summary>
    public static Result CheckMultipleComponents(JsonObject entityData, string componentType, ComponentSchemaRegistry schemaRegistry)
    {
        var getSchemaResult = schemaRegistry.GetSchemaForComponentType(componentType);
        if (getSchemaResult.IsFailure)
        {
            return Result.Fail($"Failed to get schema for component type '{componentType}'")
                .WithErrors(getSchemaResult);
        }
        var schema = getSchemaResult.Value;

        var componentTypeInfo = schema.ComponentTypeInfo;

        // Check if the component type allows multiple components

        var allowMultiple = componentTypeInfo.GetBooleanAttribute(EntityConstants.AllowMultipleComponentsKey); 
        if (!allowMultiple)
        {
            // Check if the entity data already contains a component of the same type
            var getComponentsResult = GetComponentsOfType(entityData, componentType);
            if (getComponentsResult.IsFailure)
            {
                return Result.Fail($"Failed to get components of type '{componentType}'")
                    .WithErrors(getComponentsResult);
            }
            var componentCount = getComponentsResult.Value.Count;

            if (componentCount > 1)
            {
                return Result.Fail($"Component type '{componentType}' does not allow multiple components");
            }
        }

        return Result.Ok();
    }

    /// <summary>
    /// Checks if a specific component in the entity data is valid against its respective schema.
    /// </summary>
    public static Result CheckComponentSchema(JsonObject entityData, int componentIndex, ComponentSchemaRegistry schemaRegistry)
    {
        // Get the component at specified index

        var componentPointer = JsonPointer.Parse($"/{EntityConstants.ComponentsKey}/{componentIndex}");
        if (!componentPointer.TryEvaluate(entityData, out var componentNode))
        {
            return Result.Fail($"Failed to get component at index: {componentIndex}");
        }

        if (componentNode is not JsonObject componentObject)
        {
            return Result.Fail($"Component at index {componentIndex} is not a JSON object");
        }

        // Get the component type

        var componentTypePointer = JsonPointer.Parse($"/{EntityConstants.ComponentTypeKey}");
        if (!componentTypePointer.TryEvaluate(componentObject, out var componentTypeNode) ||
            componentTypeNode is null ||
            componentTypeNode.GetValueKind() != JsonValueKind.String)
        {
            return Result.Fail($"Failed to get component type for component at index: {componentIndex}");
        }
        var typeAndVersion = componentTypeNode.GetValue<string>();

        var parseResult = EntityUtils.ParseComponentTypeAndVersion(typeAndVersion);
        if (parseResult.IsFailure)
        {
            return Result<ComponentSchema>.Fail($"Failed to parse component type and version: {typeAndVersion}");
        }
        var (componentType, componentVersion) = parseResult.Value;

        // Get the schema for the component type

        var getSchemaResult = schemaRegistry.GetSchemaForComponentType(componentType);
        if (getSchemaResult.IsFailure)
        {
            return Result.Fail($"Failed to get schema for component type '{componentType}'")
                .WithErrors(getSchemaResult);
        }
        var componentSchema = getSchemaResult.Value;

        // Validate the component against its schema

        var validateResult = componentSchema.ValidateJsonObject(componentObject);
        if (validateResult.IsFailure)
        {
            return Result.Fail($"Component at index {componentIndex} is not valid against its schema")
                .WithErrors(validateResult);
        }

        return Result.Ok();
    }

    /// <summary>
    /// Checks if all components in the entity data are valid against their respective schemas.
    /// </summary>
    public static Result CheckComponentSchemas(JsonObject entityData, ComponentSchemaRegistry schemaRegistry)
    {
        var componentsPointer = JsonPointer.Parse($"/{EntityConstants.ComponentsKey}");

        if (!componentsPointer.TryEvaluate(entityData, out var componentsNode) ||
            componentsNode is not JsonArray componentsArray ||
            componentsArray is null)
        {
            return Result.Fail("Failed to get components array");
        }

        // Validate each component in the entity against its corresponding schema
        for (int i = 0; i < componentsArray.Count; i++)
        {
            var checkSchemaResult = CheckComponentSchema(entityData, i, schemaRegistry);
            if (checkSchemaResult.IsFailure)
            {
                return Result.Fail($"Component at index {i} is not valid against its schema")
                    .WithErrors(checkSchemaResult);
            }
        }

        return Result.Ok();
    }

    /// <summary>
    /// Creates a reverse patch operation (to support undo) for the specified patch operation.
    /// </summary>
    public static Result<PatchOperation> CreateReversePatchOperation(JsonObject entityData, PatchOperation operation)
    {
        PatchOperation reverseOperation;
        switch (operation.Op)
        {
            case OperationType.Add:
                reverseOperation = PatchOperation.Remove(operation.Path);
                break;

            case OperationType.Remove:
                {
                    if (!operation.Path.TryEvaluate(entityData, out var existingValue))
                    {
                        return Result<PatchOperation>.Fail($"Component does not exist at path: '{operation.Path}'");
                    }
                    reverseOperation = PatchOperation.Add(operation.Path, existingValue);
                    break;
                }

            case OperationType.Replace:
                {
                    if (!operation.Path.TryEvaluate(entityData, out var existingValue))
                    {
                        return Result<PatchOperation>.Fail($"Component does not exist at path: '{operation.Path}'");
                    }
                    reverseOperation = PatchOperation.Replace(operation.Path, existingValue);
                    break;
                }

            default:
                return Result<PatchOperation>.Fail($"Patch operation is not supported: {operation.Op}");
        }

        return Result<PatchOperation>.Ok(reverseOperation);
    }

    public static Result<(string componentType, int version)> ParseComponentTypeAndVersion(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Result<(string, int)>.Fail($"Component type is empty");
        }

        var parts = input.Split('#');
        if (parts.Length != 2 || !int.TryParse(parts[1], out int number))
        {
            return Result<(string, int)>.Fail($"Component type '{input}' is not in the format '<Component Type>#<Version>'");
        }

        var typeAndVersion = (parts[0], number);
        return Result<(string, int)>.Ok(typeAndVersion);
    }
}
