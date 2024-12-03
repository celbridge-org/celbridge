using Celbridge.Entities.Services;
using CommunityToolkit.Diagnostics;
using Json.Patch;
using Json.Pointer;
using Json.Schema;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace Celbridge.Entities.Models;

public class EntityData
{
    public JsonObject JsonObject { get; private set; }
    public JsonSchema EntitySchema { get; }

    private EntityData(JsonObject jsonObject, JsonSchema entitySchema)
    {
        JsonObject = jsonObject;
        EntitySchema = entitySchema;
    }

    public static EntityData Create(JsonObject jsonObject, JsonSchema entitySchema)
    {
        return new EntityData(jsonObject, entitySchema);
    }

    public Result<List<int>> GetComponentsOfType(string componentType)
    {
        if (JsonObject["_components"] is not JsonArray components)
        {
            return Result<List<int>>.Fail("Entity data does not contain any components");
        }

        var indices = new List<int>();
        for (int i = 0; i < components.Count; i++)
        {
            var propertyPath = JsonPointer.Create("_components", i, "_componentType");

            var getPropertyResult = GetProperty<string>(propertyPath);
            if (getPropertyResult.IsFailure)
            {
                continue;
            }
            var componentTypeValue = getPropertyResult.Value;

            if (componentTypeValue == componentType)
            {
                indices.Add(i);
            }
        }

        return Result<List<int>>.Ok(indices);
    }

    public Result<T> GetProperty<T>(JsonPointer propertyPointer)
        where T : notnull
    {
        try
        {
            if (!propertyPointer.TryEvaluate(JsonObject, out var valueNode))
            {
                return Result<T>.Fail($"Property was not found at: '{propertyPointer}'");
            }

            if (valueNode is null)
            {
                // The property was found but it is a JSON null value.
                // We treat this as an error for Entity Data.
                return Result<T>.Fail($"Property is a JSON null value: '{propertyPointer}'");
            }

            var value = valueNode.Deserialize<T>(EntityService.SerializerOptions);
            if (value is null)
            {
                return Result<T>.Fail($"Failed to deserialize property at '{propertyPointer}' to type '{nameof(T)}'");
            }
            
            return Result<T>.Ok(value);
        }
        catch (Exception ex)
        {
            return Result<T>.Fail($"An exception occurred when getting entity property '{propertyPointer}'")
                .WithException(ex);
        }
    }

    public Result<string> GetPropertyAsJSON(JsonPointer propertyPointer)
    {
        try
        {
            if (!propertyPointer.TryEvaluate(JsonObject, out var valueNode))
            {
                return Result<string>.Fail($"Property was not found at: '{propertyPointer}'");
            }

            if (valueNode is null)
            {
                // The property was found but it is a JSON null value.
                // We treat this as an error for Entity Data.
                return Result<string>.Fail($"Property is a JSON null value: '{propertyPointer}'");
            }

            var valueJSON = valueNode.ToJsonString();

            return Result<string>.Ok(valueJSON);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"An exception occurred when getting entity property '{propertyPointer}'")
                .WithException(ex);
        }
    }

    public Result<PatchSummary> ApplyPatchOperation(ResourceKey resource, PatchOperation operation, ComponentSchemaRegistry schemaRegistry)
    {
        try
        {
            // Generate the patched version of the entity.
            // We perform a number of checks on this patched entity before we use it to replace the existing entity.

            var patch = new JsonPatch(operation);
            var patchResult = patch.Apply(JsonObject);
            if (!patchResult.IsSuccess)
            {
                return Result<PatchSummary>.Fail($"Failed to apply JSON patch to entity data: {patchResult.Error}");
            }

            var patchedJsonObject = patchResult.Result as JsonObject;
            Guard.IsNotNull(patchedJsonObject);

            // Check if the JSON object has actually changed as a result of applying the patch
            if (JsonNode.DeepEquals(JsonObject, patchedJsonObject))
            {
                // The patch was valid, but did not result in any changes.
                // This is indicated by returning null reverse patch and change message values.
                var emptyPatchSummary = new PatchSummary(operation, null, null);
                return Result<PatchSummary>.Ok(emptyPatchSummary);
            }

            // Check if the patched JSON is still valid for the entity schema

            var evaluateResult = EntitySchema.Evaluate(patchedJsonObject);
            if (!evaluateResult.IsValid)
            {
                return Result<PatchSummary>.Fail($"Failed to apply JSON patch to entity data. Schema validation error.");
            }

            // Make reverse patch and component changed message
            PatchOperation reverseOperation;
            switch (operation.Op)
            {
                case OperationType.Add:
                    reverseOperation = PatchOperation.Remove(operation.Path);
                    break;

                case OperationType.Remove:
                    {
                        if (!operation.Path.TryEvaluate(JsonObject, out var existingValue))
                        {
                            return Result<PatchSummary>.Fail($"Component does not exist at path: '{operation.Path}'");
                        }
                        reverseOperation = PatchOperation.Add(operation.Path, existingValue);
                        break;
                    }

                case OperationType.Replace:
                    {
                        if (!operation.Path.TryEvaluate(JsonObject, out var existingValue))
                        {
                            return Result<PatchSummary>.Fail($"Component does not exist at path: '{operation.Path}'");
                        }
                        reverseOperation = PatchOperation.Replace(operation.Path, existingValue);
                        break;
                    }

                case OperationType.Move:
                    reverseOperation = PatchOperation.Move(operation.Path, operation.From);
                    break;

                case OperationType.Copy:
                    reverseOperation = PatchOperation.Remove(operation.Path);
                    break;

                default:
                    return Result<PatchSummary>.Fail("Unsupported patch operation");
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
            // Removed component operations don't require validation.

            bool isRemoveComponentOperation = operation.Path.Count == 2 && operation.Op == OperationType.Remove;
            if (!isRemoveComponentOperation)
            {
                var componentIndex = componentChange.ComponentIndex;
                var componentType = componentChange.ComponentType;

                var getSchemaResult = schemaRegistry.GetSchemaForComponentType(componentType);
                if (getSchemaResult.IsFailure)
                {
                    return Result<PatchSummary>.Fail($"Failed to get schema for component type '{componentType}'")
                        .WithErrors(getSchemaResult);
                }
                var componentSchema = getSchemaResult.Value;

                var jsonPointer = JsonPointer.Parse($"/_components/{componentIndex}");
                if (!jsonPointer.TryEvaluate(patchedJsonObject, out var componentNode))
                {
                    return Result<PatchSummary>.Fail($"Failed to get component at index: {componentIndex}");
                }

                if (componentNode is not JsonObject componentObject)
                {
                    return Result<PatchSummary>.Fail($"Component at index {componentIndex} is not a JSON object");
                }

                var validateResult = componentSchema.ValidateJsonObject(componentObject);
                if (validateResult.IsFailure)
                {
                    return Result<PatchSummary>.Fail($"Component at index {componentIndex} is not valid against its schema")
                        .WithErrors(validateResult);
                }
            }

            // The patched component has passed validation, so we can now update the entity.
            JsonObject = patchedJsonObject;

            var patchSummary = new PatchSummary(operation, reverseOperation, componentChange);
            return Result<PatchSummary>.Ok(patchSummary);
        }
        catch (Exception ex)
        {
            return Result<PatchSummary>.Fail($"An exception occurred when applying JSON patch to entity data.")
                .WithException(ex);
        }
    }

    private Result<ComponentChangedMessage> GetChangeForPatchOperation(ResourceKey resource, PatchOperation operation)
    {
        try
        {
            var jsonPointer = operation.Path;

            // Check if the JSON pointer is a component property path
            if (jsonPointer.Count < 2 ||
                jsonPointer[0] != "_components")
            {
                throw new InvalidOperationException($"Component patch operation does not start with /_components");
            }

            // Extract the component index from the path
            var indexSegment = jsonPointer[1];
            if (!int.TryParse(indexSegment, out var componentIndex))
            {
                throw new InvalidOperationException($"Component patch operation does not specify a component index");
            }

            var componentType = string.Empty;
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

                var addedComponentTypeNode = addedComponent["_componentType"];
                if (addedComponentTypeNode is null)
                {
                    throw new InvalidOperationException($"Added component does not have a '_componentType' property");
                }

                componentType = addedComponentTypeNode.GetValue<string>();
            }
            else
            {
                // ...in all other cases, use the component type of the existing entity component.

                var componentTypePointer = jsonPointer[..2].Combine("_componentType");
                if (!componentTypePointer.TryEvaluate(JsonObject, out var componentTypeNode) ||
                    componentTypeNode is null)
                {
                    throw new InvalidOperationException($"Component at index {componentIndex} does not contain a '_componentType' property");
                }

                componentType = componentTypeNode.GetValue<string>();
            }

            if (string.IsNullOrEmpty(componentType))
            {
                throw new InvalidOperationException($"Invalid component type");
            }

            string propertyPath = jsonPointer.Count == 2 ? "/" : jsonPointer[2..].ToString();
            var operationName = operation.Op.ToString().ToLower();

            // Create a message for the component change
            var message = new ComponentChangedMessage(resource, componentType, componentIndex, propertyPath, operationName);

            return Result<ComponentChangedMessage>.Ok(message);
        }
        catch (Exception ex)
        {
            return Result<ComponentChangedMessage>.Fail("An exception occurred when extracting changes from a component patch operation")
                .WithException(ex);
        }
    }
}
