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
    /// Registers the folder of app-bundled web assets shared by every WebView (the celbridge-client
    /// JS, icons, fonts), served at /assets/{path}.
    /// </summary>
    void RegisterAssetsFolder(string folderPath);

    /// <summary>
    /// Registers a package's asset folder, served at /package/{name}/{path}. Re-registering a name
    /// replaces the previous folder.
    /// </summary>
    void RegisterPackageFolder(string packageName, string folderPath);

    /// <summary>
    /// Stops serving the given package and releases its file provider.
    /// </summary>
    void UnregisterPackageFolder(string packageName);

    /// <summary>
    /// Allows the given web origin to read the /assets/ and /package/ routes cross-origin. Used by a
    /// synthetic-origin editor (a faked origin for a domain-locked library) that fetches its lib and the
    /// shared client from this server. Any origin not registered here is refused cross-origin reads, so a
    /// page in another local origin (for example one loaded in the user's browser that discovered the
    /// loopback port) cannot read served files across origins. Idempotent.
    /// </summary>
    void RegisterCrossOriginReader(string origin);

    /// <summary>
    /// Builds the absolute loopback URL a WebView is navigated to for a project file, or an empty
    /// string when the server is not running. In-page references use root-relative /project/ paths.
    /// </summary>
    string GetProjectUrl(string path);

    /// <summary>
    /// Builds the absolute loopback URL for a shared bundled asset, or an empty string when the
    /// server is not running. In-page references use root-relative /assets/ paths.
    /// </summary>
    string GetAssetsUrl(string path);

    /// <summary>
    /// Builds the absolute loopback URL a WebView is navigated to for a package file, or an empty
    /// string when the server is not running. In-page references use root-relative /package/ paths.
    /// </summary>
    string GetPackageUrl(string packageName, string path);
}
