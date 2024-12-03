using CommunityToolkit.Diagnostics;
using Json.Patch;
using Json.Pointer;
using System.Text.Json.Nodes;
using System.Text.Json;
using Celbridge.Entities.Services;
using Json.Schema;

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

    public EntityData DeepClone()
    {
        var jsonClone = JsonObject.DeepClone() as JsonObject;
        Guard.IsNotNull(jsonClone);

        return new EntityData(jsonClone, EntitySchema);
    }

    public Result<string> GetComponentType(int componentIndex)
    {
        var propertyPath = $"/_components/{componentIndex}/_componentType";

        var getPropertyResult = GetProperty<string>(propertyPath);
        if (getPropertyResult.IsFailure)
        {
            return Result<string>.Fail("Failed to get component type")
                .WithErrors(getPropertyResult);
        }

        return Result<string>.Ok(getPropertyResult.Value);
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
            JsonNode? componentNode = components[i];
            if (componentNode is not JsonObject componentObject)
            {
                continue;
            }

            if (componentObject["_componentType"] is not JsonValue componentTypeNode ||
                componentTypeNode.GetValueKind() != JsonValueKind.String)
            {
                continue;
            }

            var componentTypeValue = componentTypeNode.GetValue<string>();
            if (componentTypeValue != componentType)
            {
                continue;
            }

            indices.Add(i);
        }

        return Result<List<int>>.Ok(indices);
    }

    public Result<T> GetProperty<T>(string propertyPath)
        where T : notnull
    {
        try
        {
            var jsonPointer = JsonPointer.Parse(propertyPath);
            if (jsonPointer is null)
            {
                return Result<T>.Fail($"Invalid JSON pointer '{propertyPath}'");
            }

            if (!jsonPointer.TryEvaluate(JsonObject, out var valueNode))
            {
                return Result<T>.Fail($"Property was not found at: '{propertyPath}'");
            }

            if (valueNode is null)
            {
                // The property was found but it is a JSON null value.
                // We treat this as an error for Entity Data.
                return Result<T>.Fail($"Property is a JSON null value: '{propertyPath}'");
            }

            var value = valueNode.Deserialize<T>(EntityService.SerializerOptions);
            if (value is null)
            {
                return Result<T>.Fail($"Failed to deserialize property at '{propertyPath}' to type '{nameof(T)}'");
            }
            
            return Result<T>.Ok(value);
        }
        catch (Exception ex)
        {
            return Result<T>.Fail($"An exception occurred when getting entity property '{propertyPath}'")
                .WithException(ex);
        }
    }

    public Result<string> GetPropertyAsJSON(string propertyPath)
    {
        try
        {
            var jsonPointer = JsonPointer.Parse(propertyPath);
            if (jsonPointer is null)
            {
                return Result<string>.Fail($"Invalid JSON pointer '{propertyPath}'");
            }

            if (!jsonPointer.TryEvaluate(JsonObject, out var valueNode))
            {
                return Result<string>.Fail($"Property was not found at: '{propertyPath}'");
            }

            if (valueNode is null)
            {
                // The property was found but it is a JSON null value.
                // We treat this as an error for Entity Data.
                return Result<string>.Fail($"Property is a JSON null value: '{propertyPath}'");
            }

            var valueJSON = valueNode.ToJsonString();

            return Result<string>.Ok(valueJSON);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"An exception occurred when getting entity property '{propertyPath}'")
                .WithException(ex);
        }
    }

    public Result<PatchSummary> ApplyPatch(ResourceKey resource, string patchJson, ComponentSchemaRegistry schemaRegistry)
    {
        try
        {
            var patch = JsonSerializer.Deserialize<JsonPatch>(patchJson);
            if (patch is null)
            {
                return Result<PatchSummary>.Fail("Failed to deserialize JSON patch");
            }

            // Generate the patched version of the entity.
            // We perform a number of checks on this patched entity before we use it to replace the existing entity.

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
                // This is indicated by returning an empty reverse patch and change list.
                var emptyAppliedPatch = new PatchSummary(
                    patchJson,
                    string.Empty,
                    new List<ComponentChangedMessage>());
                return Result<PatchSummary>.Ok(emptyAppliedPatch);
            }

            // Check if the patched JSON is still valid for the entity schema

            var evaluateResult = EntitySchema.Evaluate(patchedJsonObject);
            if (!evaluateResult.IsValid)
            {
                return Result<PatchSummary>.Fail($"Failed to apply JSON patch to entity data. Schema validation error.");
            }

            // Generate the reverse patch so we can undo the changes later if needed.
            var reversePatch = patchedJsonObject.CreatePatch(JsonObject);
            var reversePatchJson = JsonSerializer.Serialize(reversePatch);

            // Make a note of each component change that the patch makes to the entity data.

            var getResult = GetChangesForPatch(resource, patch);
            if (getResult.IsFailure)
            {
                return Result<PatchSummary>.Fail($"Failed to extract component changes from entity patch")
                    .WithErrors(getResult);
            }
            var componentChanges = getResult.Value;

            // Check that each component that was modified by the patch is still valid against its schema

            var validatedComponents = new HashSet<int>();
            foreach (var componentChange in componentChanges)
            {
                if (componentChange.PropertyPath == "/" &&
                    componentChange.Operation == "remove")
                {
                    // Removed components don't require validation
                    continue;
                }

                var componentIndex = componentChange.ComponentIndex;
                var componentType = componentChange.ComponentType;

                if (validatedComponents.Contains(componentIndex))
                {
                    // This component has already been validated
                    continue;
                }

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

                validatedComponents.Add(componentIndex);
            }

            // The patched entity has passed validation, so we can now update the entity.
            JsonObject = patchedJsonObject;

            var patchSummary = new PatchSummary(patchJson, reversePatchJson, componentChanges);
            return Result<PatchSummary>.Ok(patchSummary);
        }
        catch (Exception ex)
        {
            return Result<PatchSummary>.Fail($"An exception occurred when applying JSON patch to entity data.")
                .WithException(ex);
        }
    }

    private Result<List<ComponentChangedMessage>> GetChangesForPatch(ResourceKey resource, JsonPatch patch)
    {
        try
        {
            List<ComponentChangedMessage> componentChanges = new();

            foreach (var op in patch.Operations)
            {
                var jsonPointer = op.Path;
                var path = jsonPointer.ToString();

                // Check if the JSON pointer is a component property path
                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments is null ||
                    segments.Length < 2 ||
                    segments[0] != "_components")
                {
                    throw new InvalidOperationException($"Component patch operation does not start with /_components");
                }

                // Extract the component index from the path
                var indexSegment = segments[1];
                if (!int.TryParse(indexSegment, out var componentIndex))
                {
                    throw new InvalidOperationException($"Component patch operation does not specify a component index");
                }

                var componentType = string.Empty;
                if (op.Op == OperationType.Add &&
                    segments.Length == 2)
                {
                    // This is an add component operation, so extract the type of the component that will be
                    // added by the patch...

                    var addedComponent = op.Value as JsonObject;
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
                    // ...in all other cases, use the component type of the existing component.

                    var components = JsonObject["_components"] as JsonArray;
                    if (components is null ||
                        componentIndex >= components.Count)
                    {
                        throw new InvalidOperationException($"Entity data does not contain '_components' array");
                    }

                    var componentObject = components[componentIndex] as JsonObject;
                    if (componentObject is null)
                    {
                        throw new InvalidOperationException($"'_components' array does not contain a JsonObject at index: {componentIndex}");
                    }

                    var componentTypeNode = componentObject["_componentType"];
                    if (componentTypeNode is null)
                    {
                        throw new InvalidOperationException($"Component at index {componentIndex} does not contain a '_componentType' property");
                    }

                    componentType = componentTypeNode.ToString();
                }

                if (string.IsNullOrEmpty(componentType))
                {
                    throw new InvalidOperationException($"Added component's type is empty");
                }

                // Report the operation type as a string
                var operation = op.Op.ToString().ToLower();

                // Construct the component relative property path 
                string propertyPath;
                if (segments.Length == 2)
                {
                    // This is a component add, remove, etc. operation, so the property path is the root of the component
                    propertyPath = "/";
                }
                else
                {
                    // This is a component property change operation, so the property path is the component relative path
                    propertyPath = "/" + string.Join("/", segments.Skip(2));
                
                    if (operation == "add" || operation == "replace")
                    {
                        // We report 'add' and 'replace' property operations as a 'set' operation, because the distinction is not
                        // important to clients listening for property changes.
                        operation = "set";
                    }
                }

                var componentChange = new ComponentChangedMessage(resource, componentType, componentIndex, propertyPath, operation);
                componentChanges.Add(componentChange);
            }

            return Result<List<ComponentChangedMessage>>.Ok(componentChanges);
        }
        catch (Exception ex)
        {
            return Result<List<ComponentChangedMessage>>.Fail("An exception occurred when extracting component changes from patch")
                .WithException(ex);
        }
    }
}
