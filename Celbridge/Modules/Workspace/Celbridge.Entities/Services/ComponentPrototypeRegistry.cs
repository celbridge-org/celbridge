using Celbridge.Entities.Models;
using System.Text.Json.Nodes;

namespace Celbridge.Entities.Services;

public class ComponentPrototypeRegistry
{
    private readonly Dictionary<string, ComponentPrototype> _componentPrototypes = new();
    private readonly Dictionary<string, List<string>> _entityComponentTypes = new();

    public async Task<Result> LoadComponentPrototypesAsync(ComponentSchemaRegistry componentSchemaRegistry)
    {
        try
        {
            List<string> jsonContents = new List<string>();

            var configFolder = await Package.Current.InstalledLocation.GetFolderAsync(EntityService.ComponentConfigFolder);
            var prototypesFolder = await configFolder.GetFolderAsync(EntityService.PrototypesFolder);

            var prototypeFiles = await prototypesFolder.GetFilesAsync();
            foreach (var prototypeFile in prototypeFiles)
            {
                var prototypeJson = await FileIO.ReadTextAsync(prototypeFile);

                // Get the component schema specified in the protype JSON

                var getResult = componentSchemaRegistry.GetComponentSchemaFromJson(prototypeJson);
                if (getResult.IsFailure)
                {
                    return Result.Fail($"Failed to get component schema for component prototype: {prototypeFile.DisplayName}")
                        .WithErrors(getResult);
                }
                var componentSchema = getResult.Value;

                // Validate the prototype JSON against the schema

                var validateResult = componentSchema.ValidateJson(prototypeJson);
                if (validateResult.IsFailure)
                {
                    return Result.Fail($"Failed to validate component prototype: {prototypeFile.DisplayName}")
                        .WithErrors(validateResult);
                }

                var prototypeJsonNode = JsonNode.Parse(prototypeJson);
                if (prototypeJsonNode is not JsonObject prototypeJsonObject)
                {
                    return Result.Fail("Failed to parse component prototype as a JSON object");
                }

                // Get the component type

                var componentTypeValue = prototypeJsonNode["_componentType"] as JsonValue;
                if (componentTypeValue is null ||
                    !componentTypeValue.TryGetValue(out string? componentType) ||
                    string.IsNullOrEmpty(componentType))
                {
                    return Result.Fail("Component type is missing or empty");
                }

                // Create and register the prototype

                var componentPrototype = ComponentPrototype.Create(prototypeJsonObject, componentSchema);
                _componentPrototypes[componentType] = componentPrototype;
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when loading component prototypes")
                .WithException(ex);
        }
    }

    public Result<ComponentPrototype> GetPrototype(string componentType)
    {
        if (!_componentPrototypes.TryGetValue(componentType, out var componentPrototype))
        {
            return Result<ComponentPrototype>.Fail($"Component prototype for entity type '{componentType}' not found");
        }

        return Result<ComponentPrototype>.Ok(componentPrototype);
    }
}
