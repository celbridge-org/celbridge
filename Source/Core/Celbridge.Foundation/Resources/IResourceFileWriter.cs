namespace Celbridge.Resources;

/// <summary>
/// Writes file content to project resources. The single chokepoint for disk
/// writes inside the project folder: callers pass a ResourceKey and the writer
/// resolves it through IResourceRegistry.ResolveResourcePath, so containment
/// and symlink validation run automatically. Writes are atomic via temp-file
/// rename and retry on transient IO failures.
/// </summary>
public interface IResourceFileWriter
{
    /// <summary>
    /// Writes raw bytes to the resource. The destination's parent folder is
    /// created if it does not exist. Atomic via temp-file rename, with bounded
    /// retry on transient IOException.
    /// </summary>
    Task<Result> WriteAllBytesAsync(ResourceKey resource, byte[] bytes);

    /// <summary>
    /// Writes UTF-8 text (no BOM) to the resource. The destination's parent
    /// folder is created if it does not exist. Atomic via temp-file rename,
    /// with bounded retry on transient IOException. Callers are responsible
    /// for selecting line endings appropriate to the target file.
    /// </summary>
    Task<Result> WriteAllTextAsync(ResourceKey resource, string content);
}
