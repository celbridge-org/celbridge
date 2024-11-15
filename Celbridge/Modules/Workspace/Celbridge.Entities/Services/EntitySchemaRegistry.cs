using Celbridge.Entities.Models;
using System.Text.Json;

namespace Celbridge.Entities.Services;

public class EntitySchemaRegistry
{
    private const string EntityConfigFolder = "EntityConfig";
    private const string SchemasFolder = "Schemas";

    private readonly Dictionary<string, EntitySchema> _schemas = new();

    public async Task<Result> LoadSchemasAsync()
    {
        try
        {
            List<string> jsonContents = new List<string>();

            // The Uno docs only discuss using StorageFile.GetFileFromApplicationUriAsync()
            // to load files from the app package, but Package.Current.InstalledLocation appears
            // to work fine on both Windows and Skia+Gtk platforms.
            // https://platform.uno/docs/articles/features/file-management.html#support-for-storagefilegetfilefromapplicationuriasync
            var configFolder = await Package.Current.InstalledLocation.GetFolderAsync(EntityConfigFolder);
            var schemasFolder = await configFolder.GetFolderAsync(SchemasFolder);

            var jsonFiles = await schemasFolder.GetFilesAsync();
            foreach (var jsonFile in jsonFiles)
            {
                var content = await FileIO.ReadTextAsync(jsonFile);

                AddSchema(content);
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when loading schemas")
                .WithException(ex);
        }
    }

    public Result<EntitySchema> GetSchemaForEntityType(string entityType)
    {
        if (!_schemas.TryGetValue(entityType, out var entitySchema))
        {
            return Result<EntitySchema>.Fail($"Schema '{entityType}' not found");
        }

        return Result<EntitySchema>.Ok(entitySchema);
    }

    public Result<EntitySchema> GetSchemaFromJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result<EntitySchema>.Fail("Failed to parse JSON as an object");
            }

            if (!root.TryGetProperty("_entityType", out var entityTypeElement) 
                || entityTypeElement.ValueKind != JsonValueKind.String)
            {
                return Result<EntitySchema>.Fail("Entity type not found");
            }

            var entityType = entityTypeElement.GetString();
            if (string.IsNullOrEmpty(entityType))
            {
                return Result<EntitySchema>.Fail("Entity type is empty");
            }

            return GetSchemaForEntityType(entityType);
        }
        catch (Exception ex)
        {
            return Result<EntitySchema>.Fail("An exception occurred when getting schema from JSON.")
                .WithException(ex);
        }
    }

    public Result AddSchema(string schemaJson)
    {
        try
        {
            var createResult = EntitySchema.FromJson(schemaJson);
            if (!createResult.IsSuccess)
            {
                return Result.Fail("Failed to create entity schema from JSON")
                    .WithErrors(createResult);
            }

            var entitySchema = createResult.Value;

            var entityType = entitySchema.EntityType;
            if (_schemas.ContainsKey(entityType))
            {
                return Result.Fail($"Entity schema '{entityType}' already exists");
            }

            _schemas[entityType] = entitySchema;

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("An exception occurred when adding the schema.")
                .WithException(ex);
        }
    }
}
