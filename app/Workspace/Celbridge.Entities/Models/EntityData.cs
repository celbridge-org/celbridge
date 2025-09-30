using Celbridge.Entities.Services;
using Json.Patch;
using Json.Pointer;
using Json.Schema;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace Celbridge.Entities.Models;

public class EntityData
{
    public JsonObject EntityJsonObject { get; private set; }
    public JsonSchema EntitySchema { get; }
    public HashSet<string> Tags { get; }

    private EntityData(JsonObject jsonObject, JsonSchema entitySchema, HashSet<string> tags)
    {
        EntityJsonObject = jsonObject;
        EntitySchema = entitySchema;
        Tags = tags;
    }

    public static EntityData Create(JsonObject jsonObject, JsonSchema entitySchema, HashSet<string> tags)
    {
        return new EntityData(jsonObject, entitySchema, tags);
    }

    public Result<JsonNode> EvaluateJsonPointer(JsonPointer propertyPointer)
    {
        try
        {
            if (!propertyPointer.TryEvaluate(EntityJsonObject, out var valueNode))
            {
                return Result<JsonNode>.Fail($"Property was not found at: '{propertyPointer}'");
            }

            if (valueNode is null)
            {
                // The property was found but it is a JSON null value.
                // Null property values are not supported for Entity Data.
                return Result<JsonNode>.Fail($"Property is a JSON null value: '{propertyPointer}'");
            }

            return Result<JsonNode>.Ok(valueNode);
        }
        catch (Exception ex)
        {
            return Result<JsonNode>.Fail($"An exception occurred when getting entity property '{propertyPointer}'")
                .WithException(ex);
        }
    }

    public Result<PatchSummary> ApplyPatchOperation(ResourceKey resource, PatchOperation operation, ComponentConfigRegistry configRegistry, long undoGroupId)
    {
        try
        {
            // Generate the patched version of the entity.
            // We perform a number of checks on this patched entity before we use it to replace the existing entity.

            var patch = new JsonPatch(operation);
            var patchResult = patch.Apply(EntityJsonObject);
            if (!patchResult.IsSuccess)
            {
                return Result<PatchSummary>.Fail($"Failed to apply JSON patch to entity data: {patchResult.Error}");
            }

            var patchedJsonObject = patchResult.Result as JsonObject;
            Guard.IsNotNull(patchedJsonObject);

            // Check if the JSON object has actually changed as a result of applying the patch
            if (JsonNode.DeepEquals(EntityJsonObject, patchedJsonObject))
            {
                // The patch was valid, but did not result in any changes.
                // This is indicated by returning null reverse patch and change message values.
                var emptyPatchSummary = new PatchSummary(operation, null, null, 0);
                return Result<PatchSummary>.Ok(emptyPatchSummary);
            }

            // Check if the patched JSON is still valid against the entity schema

            var evaluateResult = EntitySchema.Evaluate(patchedJsonObject);
            if (!evaluateResult.IsValid)
            {
                return Result<PatchSummary>.Fail($"Failed to apply JSON patch to entity data. Schema validation error.");
            }

            // Make a note of the component change that the patch applied to the entity data.
            var getResult = GetChangeForPatchOperation(resource, operation);
            if (getResult.IsFailure)
            {
                return Result<PatchSummary>.Fail($"Failed to extract component changes from entity patch")
                    .WithErrors(getResult);
            }
            var componentChange = getResult.Value;

            // Check that the component that was modified by the operation is still valid against its schema.
            // The remove component operation doesn't require validation.
            bool isRemoveComponentOperation = operation.Path.Count == 2 && operation.Op == OperationType.Remove;
            if (!isRemoveComponentOperation)
            {
                var validateResult = EntityUtils.ValidateComponent(patchedJsonObject, componentChange.ComponentKey.ComponentIndex, configRegistry);
                if (validateResult.IsFailure)
                {
                    return Result<PatchSummary>.Fail($"Failed to validate component '{componentChange.ComponentKey}' against schema for component type '{componentChange.ComponentType}'")
                        .WithErrors(validateResult);
                }
            }

            // Make reverse patch operation for undo
            var createReverseResult = EntityUtils.CreateReversePatchOperation(EntityJsonObject, operation);
            if (createReverseResult.IsFailure)
            {
                return Result<PatchSummary>.Fail($"Failed to create reverse patch operation")
                    .WithErrors(createReverseResult);
            }
            var reverseOperation = createReverseResult.Value;

            // The patched component has passed validation, so we can now update the entity.
            EntityJsonObject = patchedJsonObject;

            // Update the entity tags if the component structure has changed
            if (componentChange.PropertyPath == "/")
            {
                var getTagsResult = EntityUtils.GetAllComponentTags(EntityJsonObject, configRegistry);
                if (getTagsResult.IsFailure)
                {
                    return Result<PatchSummary>.Fail($"Failed to get component tags for entity: {resource}")
                        .WithErrors(getTagsResult);
                }
                Tags.ReplaceWith(getTagsResult.Value);
            }

            var patchSummary = new PatchSummary(operation, reverseOperation, componentChange, undoGroupId);
            return Result<PatchSummary>.Ok(patchSummary);
        }
        catch (Exception ex)
        {
            return Result<PatchSummary>.Fail($"An exception occurred when applying JSON patch to entity data.")
                .WithException(ex);
        }
    }

    public Result<int> GetComponentCount()
    {
        var componentsPointer = JsonPointer.Parse($"/{EntityUtils.ComponentsKey}");
        if (componentsPointer.TryEvaluate(EntityJsonObject, out var componentsNode) &&
            componentsNode is JsonArray componentsArray &&
            componentsArray is not null)
        {
            return Result<int>.Ok(componentsArray.Count);
        }

        return Result<int>.Fail("Failed to get component count from entity data");
    }

    private Result<ComponentChangedMessage> GetChangeForPatchOperation(ResourceKey resource, PatchOperation operation)
    {
        try
        {
            var jsonPointer = operation.Path;

            // Check if the JSON pointer is a component property path
            if (jsonPointer.Count < 2 ||
                jsonPointer[0] != EntityUtils.ComponentsKey)
            {
                throw new InvalidOperationException($"Component patch operation does not start with /{EntityUtils.ComponentsKey}");
            }

            // Extract the component index from the path
            var indexSegment = jsonPointer[1];
            if (!int.TryParse(indexSegment, out var componentIndex))
            {
                throw new InvalidOperationException($"Component patch operation does not specify a component index");
            }

            string typeAndVersion = string.Empty;
            if (operation.Op == OperationType.Add &&
                jsonPointer.Count == 2)
            {
                // This is an add component operation, so extract the type of the component that will be
                // added by the patch...

                var addedComponent = operation.Value as JsonObject;
                if (addedComponent is null)
                {
                    throw new InvalidOperationException($"Added component is not a JsonObject");
                }

                var addedComponentTypeNode = addedComponent[EntityUtils.ComponentTypeKey];
                if (addedComponentTypeNode is null)
                {
                    throw new InvalidOperationException($"Added component does not have a '{EntityUtils.ComponentTypeKey}' property");
                }

                typeAndVersion = addedComponentTypeNode.GetValue<string>();
            }
            else
            {
                // ...in all other cases, use the component type of the existing entity component.

                var componentTypePointer = jsonPointer[..2].Combine(EntityUtils.ComponentTypeKey);
                if (!componentTypePointer.TryEvaluate(EntityJsonObject, out var componentTypeNode) ||
                    componentTypeNode is null)
                {
                    throw new InvalidOperationException($"Component at index {componentIndex} does not contain a '{EntityUtils.ComponentTypeKey}' property");
                }

                typeAndVersion = componentTypeNode.GetValue<string>();
            }

            var parseResult = EntityUtils.ParseComponentTypeAndVersion(typeAndVersion);
            if (parseResult.IsFailure)
            {
                throw new InvalidOperationException($"Failed to parse component type and version: {typeAndVersion}. {parseResult.Error}");
            }
            var (componentType, _) = parseResult.Value;

            if (string.IsNullOrEmpty(componentType))
            {
                throw new InvalidOperationException($"Invalid component type");
            }

            string propertyPath = jsonPointer.Count == 2 ? "/" : jsonPointer[2..].ToString();
            var operationName = operation.Op.ToString().ToLower();

            // Create a message for the component change
            var componentKey = new ComponentKey(resource, componentIndex);
            var message = new ComponentChangedMessage(componentKey, componentType, propertyPath, operationName);

            return Result<ComponentChangedMessage>.Ok(message);
        }
        catch (Exception ex)
        {
            return Result<ComponentChangedMessage>.Fail("An exception occurred when extracting changes from a component patch operation")
                .WithException(ex);
        }
    }
}
