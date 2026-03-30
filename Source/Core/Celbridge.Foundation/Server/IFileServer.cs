namespace Celbridge.Server;

/// <summary>
/// Serves local files over HTTP on localhost via the server's Kestrel
/// instance. Provides URL resolution for resource keys, supporting both
/// absolute keys and relative paths from a context resource.
/// </summary>
public interface IFileServer
{
    /// <summary>
    /// Whether the file server is enabled and ready to serve files.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Enables file serving for the given project folder and port.
    /// </summary>
    void Enable(string projectFolderPath, int port);

    /// <summary>
    /// Disables file serving and releases the file provider.
    /// </summary>
    void Disable();

    /// <summary>
    /// Resolves a path to a localhost URL for the file server.
    /// The path can be an absolute resource key (e.g. "Project/output/index.html"),
    /// a relative path from the context resource's folder (e.g. "index.html" or
    /// "../shared/header.html"), or a path with "." and ".." segments.
    /// Relative paths are resolved from the folder containing contextResource.
    /// Returns the URL if the path maps to a valid project file, or an empty
    /// string if the server is not available or the path cannot be resolved.
    /// </summary>
    string ResolveLocalFileUrl(string path, ResourceKey contextResource = default);
}
