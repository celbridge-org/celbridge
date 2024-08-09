using CommunityToolkit.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace Celbridge.Resources.Services;

public record IconDefinition(string Character, string Color, string FontSize);

public class ResourceIconService : IResourceIconService
{
    public ResourceIconService()
    {}

    public Result LoadIconDefinitions()
    {
        var loadResult = LoadIconData();
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load icon definition. {loadResult.Error}");
        }
        var iconData = loadResult.Value;

        var iconDefinitions = iconData["iconDefinitions"];
        var fileExtensions = iconData["fileExtensions"];

        // Todo: populate dictionaries

        return Result.Ok();
    }

    private Result<JObject> LoadIconData()
    {
        var resourceName = "Assets.file-icons-icon-theme.json";
        var loadResult = LoadIconDataResource(resourceName);
        if (loadResult.IsFailure)
        {
            return Result<JObject>.Fail($"Failed to load icon data from resource '{resourceName}'. Error: {loadResult.Error}");
        }
        var stream = loadResult.Value;

        try
        {
            using (var reader = new StreamReader(stream))
            {
                var json = reader.ReadToEnd();
                var jo = JObject.Parse(json);

                return Result<JObject>.Ok(jo);
            }
        }
        catch (JsonReaderException jex)
        {
            return Result<JObject>.Fail($"JSON Parsing Error while loading icon data: {jex.Message}");
        }
        catch (IOException ioex)
        {
            return Result<JObject>.Fail($"IO Error while reading icon data: {ioex.Message}");
        }
        catch (Exception ex)
        {
            return Result<JObject>.Fail($"Unexpected error while loading icon data: {ex.Message}");
        }
    }

    private Result<Stream> LoadIconDataResource(string searchResourceName)
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        Guard.IsNotNull(entryAssembly);

        // The name is prepended with the namespace which includes the platform.
        // e.g. On Windows the name starts with "Celbridge.Windows." on Windows.
        // To work around this we look for a resource that ends with the requested resource name.

        string resourceName = string.Empty;
        string[] names = entryAssembly.GetManifestResourceNames();
        foreach (var name in names)
        {
            if (name.EndsWith(searchResourceName))
            {
                resourceName = name;
                break;
            }
        }

        if (string.IsNullOrEmpty(resourceName))
        {
            return Result<Stream>.Fail($"Resource '{resourceName}' not found.");
        }

        var resourceStream = entryAssembly.GetManifestResourceStream(resourceName);
        if (resourceStream == null)
        {
            return Result<Stream>.Fail($"Failed to load resource '{resourceName}'.");
        }

        return Result<Stream>.Ok(resourceStream);
    }
}
