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

    /// <summary>
    /// The per-session token that the host-asset routes require as a query parameter. It gates the
    /// loopback host routes so other local processes cannot read served content over the socket.
    /// </summary>
    string HostAccessToken { get; }

    /// <summary>
    /// Registers a folder to be served over loopback under the given ".celbridge" host name. This is
    /// the macOS replacement for SetVirtualHostNameToFolderMapping, which is a no-op on the Skia head.
    /// Re-registering a host name replaces the previous folder.
    /// </summary>
    void RegisterHostFolder(string hostName, string folderPath);

    /// <summary>
    /// Stops serving the given host name and releases its file provider.
    /// </summary>
    void UnregisterHostFolder(string hostName);

    /// <summary>
    /// Resolves a loopback URL (including the access token) for a file under a registered host, or an
    /// empty string when the server is not running or the host is not registered.
    /// </summary>
    string ResolveHostFileUrl(string hostName, string path);
}
