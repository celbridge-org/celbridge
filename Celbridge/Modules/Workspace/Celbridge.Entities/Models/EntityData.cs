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

            var patchResult = patch.Apply(JsonObject);
            if (!patchResult.IsSuccess)
            {
                return Result<PatchSummary>.Fail($"Failed to apply JSON patch to entity data: {patchResult.Error}");
            }

            var newJsonObject = patchResult.Result as JsonObject;
            Guard.IsNotNull(newJsonObject);

            // Check if the JSON object has actually changed as a result of applying the patch
            if (JsonNode.DeepEquals(JsonObject, newJsonObject))
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

            var evaluateResult = EntitySchema.Evaluate(newJsonObject);
            if (!evaluateResult.IsValid)
            {
                return Result<PatchSummary>.Fail($"Failed to apply JSON patch to entity data. Schema validation error.");
            }

            // Generate the reverse patch so we can undo the change if needed.
            var reversePatch = newJsonObject.CreatePatch(JsonObject);
            var reversePatchJson = JsonSerializer.Serialize(reversePatch);

            // Note each component that was actually changed by the patch, filtering out any
            // operations in the original patch that did not result in a change.

            var getResult = GetComponentChangesForPatch(resource, reversePatch);
            if (getResult.IsFailure)
            {
                return Result<PatchSummary>.Fail($"Failed to extract component changes from entity patch")
                    .WithErrors(getResult);
            }
            var componentChanges = getResult.Value;

            // Check that each modified component is still valid against its schema
            var validatedComponents = new HashSet<int>();
            foreach (var componentChange in componentChanges)
            {
                var componentIndex = componentChange.ComponentIndex;
                var componentType = componentChange.ComponentType;

                if (validatedComponents.Contains(componentIndex))
                {
                    // We've already validated this component
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
                if (!jsonPointer.TryEvaluate(newJsonObject, out var componentNode))
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

            // Use the patched JSON object
            JsonObject = newJsonObject;

            var patchSummary = new PatchSummary(patchJson, reversePatchJson, componentChanges);
            return Result<PatchSummary>.Ok(patchSummary);
        }
        catch (Exception ex)
        {
            return Result<PatchSummary>.Fail($"An exception occurred when applying JSON patch to entity data.")
                .WithException(ex);
        }
    }

    private Result<List<ComponentChangedMessage>> GetComponentChangesForPatch(ResourceKey resource, JsonPatch reversePatch)
    {
        List<ComponentChangedMessage> componentChanges = new();
        reversePatch.Operations.ForEach(op =>
        {
            var jsonPointer = op.Path;
            var path = jsonPointer.ToString();

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments is null ||
                segments.Length <= 2 ||
                segments[0] != "_components")
            {
                // Todo: Log an error
                return;
            }
            var indexSegment = segments[1];
            if (!int.TryParse(indexSegment, out var componentIndex))
            {
                // Todo: Log an error
                return;
            }

            var components = JsonObject["_components"] as JsonArray;
            if (components is null ||
                componentIndex >= components.Count)
            {
                // Todo: Log an error
                return;
            }

            var componentObject = components[componentIndex] as JsonObject;
            if (componentObject is null)
            {
                // Todo: Log an error
                return;
            }

            var componentTypeNode = componentObject["_componentType"];
            if (componentTypeNode is null)
            {
                // Todo: Log an error
                return;
            }

            var componentType = componentTypeNode.ToString();

            // Construct the component relative property path 
            string propertyPath = "/" + string.Join("/", segments.Skip(2));

            var componentChange = new ComponentChangedMessage(resource, componentType, componentIndex, propertyPath);
            componentChanges.Add(componentChange);
        });

        return Result<List<ComponentChangedMessage>>.Ok(componentChanges);
    }
}
