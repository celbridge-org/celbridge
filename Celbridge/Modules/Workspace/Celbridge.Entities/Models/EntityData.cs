using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Patch;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Entities.Models;

public class EntityData
{
    public JsonObject JsonObject { get; private set; }
    public EntitySchema EntitySchema { get; }

    private EntityData(JsonObject jsonObject, EntitySchema entitySchema)
    {
        JsonObject = jsonObject;
        EntitySchema = entitySchema;
    }

    public static EntityData Create(JsonObject jsonObject, EntitySchema entitySchema)
    {
        return new EntityData(jsonObject, entitySchema);
    }

    public EntityData DeepClone()
    {
        var jsonClone = JsonObject.DeepClone() as JsonObject;
        Guard.IsNotNull(jsonClone);

        return new EntityData(jsonClone, EntitySchema);
    }

    public T? GetProperty<T>(string propertyName, T? defaultValue)
    where T : notnull
    {
        var getResult = GetPropertyChecked<T>(propertyName);
        if (getResult.IsFailure)
        {
            return defaultValue;
        }

        return getResult.Value;
    }

    public bool SetProperty<T>(string propertyName, T newValue)
        where T : notnull
    {
        var setResult = SetPropertyChecked(propertyName, newValue);
        if (setResult.IsFailure)
        {
            var failure = Result<T>.Fail($"Failed to set property '{propertyName}'")
                .WithErrors(setResult);

            throw new InvalidOperationException(failure.Error);
        }

        return setResult.Value;
    }

    private Result<T> GetPropertyChecked<T>(string propertyName)
        where T : notnull
    {
        try
        {
            if (JsonObject.ContainsKey(propertyName))
            {
                var valueNode = JsonObject[propertyName];
                if (valueNode != null)
                {
                    T? value = valueNode.Deserialize<T>();
                    if (value is not null)
                    {
                        return Result<T>.Ok(value);
                    }
                }
            }

            // Property value not found
            return Result<T>.Fail();
        }
        catch (Exception ex)
        {
            return Result<T>.Fail($"An exception occurred when getting entity property '{propertyName}'")
                .WithException(ex);
        }
    }

    private Result<bool> SetPropertyChecked<T>(string propertyName, T newValue) where T : notnull
    {
        try
        {
            var oldNode = JsonObject[propertyName];
            var newNode = JsonNode.Parse(JsonSerializer.Serialize(newValue));

            if (oldNode is null && newNode is null)
            {
                // No change
                return Result<bool>.Ok(false);
            }

            if (newNode is not null && oldNode is not null &&
                newNode.GetValueKind() == oldNode.GetValueKind() &&
                newNode.ToJsonString() == oldNode.ToJsonString())
            {
                // No change
                return Result<bool>.Ok(false);
            }

            // Update the JSON data
            JsonObject[propertyName] = newNode;

            return Result<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Fail($"Failed to set entity property '{propertyName}'")
                .WithException(ex);
        }
    }

    public Result ApplyPatch(string patchJson)
    {
        try
        {
            var patch = JsonSerializer.Deserialize<JsonPatch>(patchJson);
            if (patch is null)
            {
                return Result.Fail("Failed to deserialize JSON patch");
            }

            var patchResult = patch.Apply(JsonObject);
            if (!patchResult.IsSuccess)
            {
                return Result.Fail($"Failed to apply JSON patch to entity data: {patchResult.Error}");
            }

            // Todo: Check if the patch is valid for the entity schema before really applying it
            // Todo: Check if the json object has actually changed (return a bool)

            var jsonObject = patchResult.Result as JsonObject;
            Guard.IsNotNull(jsonObject);
            JsonObject = jsonObject;

            return Result.Ok(); 
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when applying JSON patch to entity data.")
                .WithException(ex);
        }
    }
}
