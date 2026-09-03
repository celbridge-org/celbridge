using System.Text.Json;
using Tomlyn;

namespace Celbridge.Packages.Helpers;

/// <summary>
/// Deserializes a manifest file into its typed model, reporting a TOML syntax or value error as a
/// failure that names the manifest.
/// </summary>
internal static class ManifestDeserializer
{
    // Manifest keys are the kebab-case spelling of the model's property names. Every key outside that
    // set lands in the model's UnknownKeys bag rather than being dropped.
    private static readonly TomlSerializerOptions ManifestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower
    };

    public static Result<T> Deserialize<T>(string toml, string manifestPath) where T : class
    {
        try
        {
            var manifest = TomlSerializer.Deserialize<T>(toml, ManifestOptions);
            if (manifest is null)
            {
                return Result<T>.Fail($"Failed to deserialize manifest: {manifestPath}");
            }

            return Result<T>.Ok(manifest);
        }
        catch (TomlException exception)
        {
            // A shape error carries no diagnostic, only a message, so fall back to it.
            var detail = exception.Message;
            if (exception.Diagnostics.Count > 0)
            {
                detail = string.Join("; ", exception.Diagnostics.Select(diagnostic => diagnostic.ToString()));
            }

            return Result<T>.Fail($"TOML error in {manifestPath}: {detail}");
        }
    }
}
