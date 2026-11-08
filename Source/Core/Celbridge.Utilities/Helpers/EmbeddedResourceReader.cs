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
        return Read(assembly, resourceName, stream =>
        {
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });
    }

    /// <summary>
    /// Reads an embedded resource as bytes. The resource name follows the same form as for ReadText.
    /// </summary>
    public static Result<byte[]> ReadBytes(Assembly assembly, string resourceName)
    {
        return Read(assembly, resourceName, stream =>
        {
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        });
    }

    private static Result<T> Read<T>(Assembly assembly, string resourceName, Func<Stream, T> read)
        where T : notnull
    {
        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return Result<T>.Fail($"Embedded resource not found: '{resourceName}'");
            }

            return Result<T>.Ok(read(stream));
        }
        catch (Exception ex)
        {
            return Result<T>.Fail($"Failed to read embedded resource: '{resourceName}'")
                .WithException(ex);
        }
    }
}
