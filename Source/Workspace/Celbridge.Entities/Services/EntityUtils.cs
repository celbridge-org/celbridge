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
    /// JSON key for components array.
    /// </summary>
    public const string ComponentsKey = "_components";

    /// <summary>
    /// JSON key for component type string.
    /// </summary>
    public const string ComponentTypeKey = "_type";

    public static Result<EntityData> CreateEntityData(ResourceKey resource, ComponentConfigRegistry configRegistry, JsonSchema entitySchema)
    {
        var entityJsonObject = new JsonObject
        {
            ["_entityVersion"] = 1,
            ["_components"] = new JsonArray()
        };

        var evaluateResult = entitySchema.Evaluate(JsonDocument.Parse(entityJsonObject.ToJsonString()).RootElement);
        if (!evaluateResult.IsValid)
        {
            return Result<EntityData>.Fail($"Failed to create entity data. Schema validation error: {resource}");
        }

        // This a new entity with no components, so it doesn't have any tags yet
        var tags = new HashSet<string>();

        var entityData = EntityData.Create(entityJsonObject, entitySchema, tags);

        return Result<EntityData>.Ok(entityData);
    }

    /// <summary>
    /// Loads and validates an EntityData object from a json file.
    /// </summary>
    public static Result<EntityData> LoadEntityDataFile(string entityDataPath, JsonSchema entitySchema, ComponentConfigRegistry configRegistry)
    {
        // Load the EntityData json
        var jsonObject = JsonNode.Parse(File.ReadAllText(entityDataPath)) as JsonObject;
        if (jsonObject is null)
        {
            return Result<EntityData>.Fail($"Failed to parse entity data from file: '{entityDataPath}'");
        }

        // Attempt to repair/migrate the data instead of just failing
        var migrateResult = MigrateEntityData(jsonObject);
        if (migrateResult.IsFailure)
        {
            return Result<EntityData>.Fail($"Failed to migrate entity data: '{entityDataPath}'")
                .WithErrors(migrateResult);
        }

        // Validate the loaded data against the entity schema
        var evaluateResult = entitySchema.Evaluate(JsonDocument.Parse(jsonObject.ToJsonString()).RootElement);
        if (!evaluateResult.IsValid)
        {
            return Result<EntityData>.Fail($"Entity data failed schema validation: '{entityDataPath}'");
        }

        var validateComponentsResult = ValidateEntityComponents(jsonObject, configRegistry);
        if (validateComponentsResult.IsFailure)
        {
            return Result<EntityData>.Fail($"Failed to validate components for entity: '{entityDataPath}'")
                .WithErrors(validateComponentsResult);
        }

        var getTagsResult = GetAllComponentTags(jsonObject, configRegistry);
        if (getTagsResult.IsFailure)
        {
            return Result<EntityData>.Fail($"Failed to get component tags for entity: '{entityDataPath}'")
                .WithErrors(getTagsResult);
        }
        var tags = getTagsResult.Value;

        // We've passed validation so now we can create and return the EntityData object
        var entityData = EntityData.Create(jsonObject, entitySchema, tags);

        return Result<EntityData>.Ok(entityData);
    }

    /// <summary>
    /// Returns a list of indices of components of the specified type in the entity data.
    /// </summary>
    public static Result<List<int>> GetComponentsOfType(JsonObject entityData, string componentType)
    {
        if (entityData[ComponentsKey] is not JsonArray components)
        {
            return Result<List<int>>.Fail("Entity data does not contain any components");
        }

        var indices = new List<int>();
        for (int i = 0; i < components.Count; i++)
        {
            var propertyPath = JsonPointer.Create(ComponentsKey, i, ComponentTypeKey);
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
    /// Checks if a specific component in the entity data is valid against its respective schema.
    /// </summary>
    public static Result ValidateComponent(JsonObject entityData, int componentIndex, ComponentConfigRegistry configRegistry)
    {
        var getComponentResult = GetComponentAndConfig(entityData, componentIndex, configRegistry);
        if (getComponentResult.IsFailure)
        {
            return Result.Fail($"Failed to get component and config");
        }
        var (componentObject, componentConfig) = getComponentResult.Value;

        // Validate the component against its schema
        var validationResult = componentConfig.JsonSchema.Evaluate(JsonDocument.Parse(componentObject.ToJsonString()).RootElement);
        if (!validationResult.IsValid)
        {
            return Result.Fail($"Component is not valid against its schema at index {componentIndex}");
        }

        return Result.Ok();
    }

    /// <summary>
    /// Returns the set of component tags associated with a component.
    /// </summary>
    public static Result<IReadOnlySet<string>> GetComponentTags(JsonObject entityData, int componentIndex, ComponentConfigRegistry configRegistry)
    {
        var getComponentResult = GetComponentAndConfig(entityData, componentIndex, configRegistry);
        if (getComponentResult.IsFailure)
        {
            return Result<IReadOnlySet<string>>.Fail($"Failed to get component and config");
        }
        var (componentObject, componentConfig) = getComponentResult.Value;

        return Result<IReadOnlySet<string>>.Ok(componentConfig.ComponentSchema.Tags);
    }

    private static Result<(JsonObject, ComponentConfig)> GetComponentAndConfig(JsonObject entityData, int componentIndex, ComponentConfigRegistry configRegistry)
    {
        // Get the component at specified index

        var componentPointer = JsonPointer.Parse($"/{ComponentsKey}/{componentIndex}");
        if (!componentPointer.TryEvaluate(entityData, out var componentNode))
        {
            return Result<(JsonObject, ComponentConfig)>.Fail($"Failed to get component at index: {componentIndex}");
        }

        if (componentNode is not JsonObject componentObject)
        {
            return Result<(JsonObject, ComponentConfig)>.Fail($"Component is not a JSON object at index: {componentIndex}");
        }

        // Get the component type

        var componentTypePointer = JsonPointer.Parse($"/{ComponentTypeKey}");
        if (!componentTypePointer.TryEvaluate(componentObject, out var componentTypeNode) ||
            componentTypeNode is null ||
            componentTypeNode.GetValueKind() != JsonValueKind.String)
        {
            return Result<(JsonObject, ComponentConfig)>.Fail($"Failed to get component type for component at index: {componentIndex}");
        }
        var typeAndVersion = componentTypeNode.GetValue<string>();

        var parseResult = ParseComponentTypeAndVersion(typeAndVersion);
        if (parseResult.IsFailure)
        {
            return Result<(JsonObject, ComponentConfig)>.Fail($"Failed to parse component type and version: '{typeAndVersion}'")
                .WithErrors(parseResult);
        }
        var (componentType, componentVersion) = parseResult.Value;

        // Get the component config for the component type

        var getConfigResult = configRegistry.GetComponentConfig(componentType);
        if (getConfigResult.IsFailure)
        {
            return Result<(JsonObject, ComponentConfig)>.Fail($"Failed to get component config for component type '{componentType}'")
                .WithErrors(getConfigResult);
        }
        var config = getConfigResult.Value;

        var result = (componentObject, config);

        return Result<(JsonObject, ComponentConfig)>.Ok(result);
    }

    /// <summary>
    /// Checks if all components in the entity data are valid against their respective schemas.
    /// </summary>
    public static Result ValidateEntityComponents(JsonObject entityData, ComponentConfigRegistry configRegistry)
    {
        var componentsPointer = JsonPointer.Parse($"/{ComponentsKey}");

        if (!componentsPointer.TryEvaluate(entityData, out var componentsNode) ||
            componentsNode is not JsonArray componentsArray ||
            componentsArray is null)
        {
            return Result.Fail("Failed to get components array");
        }

        // Validate each component in the entity against its corresponding schema
        for (int i = 0; i < componentsArray.Count; i++)
        {
            var validateResult = ValidateComponent(entityData, i, configRegistry);
            if (validateResult.IsFailure)
            {
                return Result.Fail($"Component is not valid against its schema at index {i}")
                    .WithErrors(validateResult);
            }
        }

        return Result.Ok();
    }

    /// <summary>
    /// Returns the union of all the component tags for an entity.
    /// </summary>
    public static Result<HashSet<string>> GetAllComponentTags(JsonObject entityData, ComponentConfigRegistry configRegistry)
    {
        var componentsPointer = JsonPointer.Parse($"/{ComponentsKey}");

        if (!componentsPointer.TryEvaluate(entityData, out var componentsNode) ||
            componentsNode is not JsonArray componentsArray ||
            componentsArray is null)
        {
            return Result<HashSet<string>>.Fail("Failed to get components array");
        }

        var allTags = new HashSet<string>();
        for (int i = 0; i < componentsArray.Count; i++)
        {
            var getTagsResult = GetComponentTags(entityData, i, configRegistry);
            if (getTagsResult.IsFailure)
            {
                return Result<HashSet<string>>.Fail($"Failed to get tags for component at index {i}")
                    .WithErrors(getTagsResult);
            }
            var componentTags = getTagsResult.Value;

            if (componentTags.Count > 0)
            {
                allTags.AddRange(componentTags);
            }
        }

        return Result<HashSet<string>>.Ok(allTags);
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

    private static Result MigrateEntityData(JsonObject entityData)
    {
        // Todo: Replace this collection of random fixups with a robust migration system

        // Remove the old "_activity" property if it exists
        if (!entityData.ContainsKey("_activity"))
        {
            entityData.Remove("_activity");
        }

        return Result.Ok();
    }
}
