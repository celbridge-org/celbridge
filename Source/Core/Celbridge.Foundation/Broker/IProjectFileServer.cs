namespace Celbridge.Broker;

/// <summary>
/// Serves project files over HTTP on localhost via the broker's Kestrel
/// server. Provides URL resolution for resource keys, supporting both
/// absolute keys and relative paths from a context resource.
/// </summary>
public interface IProjectFileServer
{
    /// <summary>
    /// Whether the project file server is enabled and ready to serve files.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Resolves a path to a localhost URL for the project file server.
    /// The path can be an absolute resource key (e.g. "Project/output/index.html"),
    /// a relative path from the context resource's folder (e.g. "index.html" or
    /// "../shared/header.html"), or a path with "." and ".." segments.
    /// Relative paths are resolved from the folder containing contextResource.
    /// Returns the URL if the path maps to a valid project file, or an empty
    /// string if the server is not available or the path cannot be resolved.
    /// </summary>
    string ResolveProjectFileUrl(string path, ResourceKey contextResource = default);
}
