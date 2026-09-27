using System.Reflection;

namespace Celbridge.Utilities;

/// <summary>
/// Reads files embedded in an assembly at build time. A resource that cannot be opened is a build
/// problem rather than a runtime condition, so it comes back as a failure naming the resource.
/// </summary>
public static class EmbeddedResourceReader
{
    /// <summary>
    /// Reads an embedded resource as text. The resource name is the assembly's root namespace
    /// followed by the file's folder path and file name, separated by dots.
    /// </summary>
    public static Result<string> ReadText(Assembly assembly, string resourceName)
    {
        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return Result<string>.Fail($"Embedded resource not found: '{resourceName}'");
            }

            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();

            return text;
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to read embedded resource: '{resourceName}'")
                .WithException(ex);
        }
    }
}
