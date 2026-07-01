using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Celbridge.Server.Services;

/// <summary>
/// Serves project files over HTTP on localhost via the server's Kestrel instance.
/// Files are served at /local/{resourceKey} and the file provider is swapped
/// when projects are loaded and unloaded.
/// </summary>
public class FileServer : IFileServer, IDisposable
{
    private readonly ILogger<FileServer> _logger;

    // The app-bundled web assets shared by every WebView, served at /assets/{path}.
    private PhysicalFileProvider? _assetsFileProvider;

    // Per-package asset folders served at /package/{name}/{path}, keyed by package name.
    private readonly ConcurrentDictionary<string, PhysicalFileProvider> _packageFileProviders = new();

    // Web origins permitted to read /assets/ and /package/ cross-origin (the synthetic-origin editors).
    // Any origin not in this set is refused cross-origin reads, so an external page cannot read served
    // files even if it discovers the loopback port. Keyed case-insensitively; the value is unused.
    private readonly ConcurrentDictionary<string, byte> _crossOriginReaders = new(StringComparer.OrdinalIgnoreCase);

    private PhysicalFileProvider? _projectFileProvider;
    private int _port;
    private bool _disposed;

    public bool IsReady => _port != 0 && _projectFileProvider is not null;

    public FileServer(ILogger<FileServer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers the /local/{path} endpoint on the given WebApplication.
    /// Must be called during Kestrel setup before the server starts.
    /// </summary>
    public void ConfigureEndpoints(WebApplication application)
    {
        application.MapGet("/local/{**path}", async (HttpContext context, string path) =>
        {
            if (_projectFileProvider is null)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("No project is currently loaded");
                return;
            }

            var fileInfo = _projectFileProvider.GetFileInfo(path);
            if (!fileInfo.Exists || fileInfo.IsDirectory)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var contentTypeProvider = new FileExtensionContentTypeProvider();
            if (!contentTypeProvider.TryGetContentType(path, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            context.Response.ContentType = contentType;

            // These headers enable support for running a WebContainer in local HTML/JS
            context.Response.Headers["Cross-Origin-Embedder-Policy"] = "credentialless";
            context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";

            await context.Response.SendFileAsync(fileInfo);
        });

        // The three WebView content routes. WebViews are navigated to a loopback URL under one of
        // these and reference everything else root-relative, so the page resolves all content against
        // its own loopback origin.

        // /project/ serves the open project's files. Its pages fetch their own content same-origin, so it
        // opts out of cross-origin reads: a page in another local origin (e.g. one loaded in the user's
        // browser that discovered the loopback port) cannot read the project's files across origins.
        application.MapGet("/project/{**path}", (HttpContext context, string path) =>
            ServeFromProvider(context, _projectFileProvider, path, allowCrossOrigin: false));

        // /assets/ and /package/ serve bundled shared assets and package folders. The synthetic-origin
        // editor (a faked origin for a domain-locked library) pulls its lib and the shared client from
        // these routes cross-origin, so they opt in to cross-origin reads.
        application.MapGet("/assets/{**path}", (HttpContext context, string path) =>
            ServeFromProvider(context, _assetsFileProvider, path, allowCrossOrigin: true));

        application.MapGet("/package/{name}/{**path}", (HttpContext context, string name, string path) =>
        {
            _packageFileProviders.TryGetValue(name, out var packageFileProvider);
            return ServeFromProvider(context, packageFileProvider, path, allowCrossOrigin: true);
        });
    }

    private async Task ServeFromProvider(HttpContext context, PhysicalFileProvider? fileProvider, string path, bool allowCrossOrigin)
    {
        if (fileProvider is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var fileInfo = fileProvider.GetFileInfo(path);
        if (!fileInfo.Exists
            || fileInfo.IsDirectory)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var contentTypeProvider = new FileExtensionContentTypeProvider();
        if (!contentTypeProvider.TryGetContentType(path, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        context.Response.ContentType = contentType;

        // Loopback-served pages are same-origin and need no CORS. Only a synthetic-origin editor (loaded
        // under a faked origin for a domain-locked library) reads across origins: it pulls its lib and the
        // shared client cross-origin from this server. Echo the CORS grant only for a registered reader
        // origin, never for "*", so an external page in another local origin cannot read served files even
        // if it discovers the loopback port. The project route never opts in at all.
        if (allowCrossOrigin)
        {
            var requestOrigin = context.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(requestOrigin)
                && _crossOriginReaders.ContainsKey(requestOrigin))
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = requestOrigin;
                context.Response.Headers["Vary"] = "Origin";
                context.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            }
        }

        // These match the /local/ route and enable WebContainer support in local HTML/JS.
        context.Response.Headers["Cross-Origin-Embedder-Policy"] = "credentialless";
        context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";

        await context.Response.SendFileAsync(fileInfo);
    }

    /// <summary>
    /// Enables file serving for the given project folder and port.
    /// </summary>
    public void Enable(string projectFolderPath, int port)
    {
        _projectFileProvider?.Dispose();
        // PhysicalFileProvider rejects path traversal but follows symlinks, so a symlink inside a served
        // root that points outside it would be served. The served roots are user-owned content, so this
        // stays within the loopback threat model.
        _projectFileProvider = new PhysicalFileProvider(projectFolderPath);
        _port = port;
        _logger.LogInformation("Project file serving enabled for {FolderPath}", projectFolderPath);
    }

    /// <summary>
    /// Disables file serving and releases the file provider.
    /// </summary>
    public void Disable()
    {
        _projectFileProvider?.Dispose();
        _projectFileProvider = null;
        _port = 0;
    }

    public string ResolveLocalFileUrl(string path, ResourceKey contextResource = default)
    {
        if (_port == 0 || _projectFileProvider is null || string.IsNullOrWhiteSpace(path))
        {
            _logger.LogDebug(
                "ResolveProjectFileUrl early exit: port={Port}, provider={HasProvider}, path='{Path}'",
                _port, _projectFileProvider is not null, path);
            return string.Empty;
        }

        // Try resolving relative to the context resource's folder first
        if (!contextResource.IsEmpty)
        {
            var contextFolder = contextResource.GetParent().ToString();
            var relativePath = CombineAndNormalize(contextFolder, path);

            if (!string.IsNullOrEmpty(relativePath))
            {
                var fileInfo = _projectFileProvider.GetFileInfo(relativePath);
                if (fileInfo.Exists && !fileInfo.IsDirectory)
                {
                    return $"http://127.0.0.1:{_port}/local/{relativePath}";
                }
            }
        }

        // Try as an absolute resource key
        var normalizedPath = NormalizePath(path);

        if (!string.IsNullOrEmpty(normalizedPath))
        {
            var fileInfo = _projectFileProvider.GetFileInfo(normalizedPath);
            if (fileInfo.Exists && !fileInfo.IsDirectory)
            {
                return $"http://127.0.0.1:{_port}/local/{normalizedPath}";
            }
        }

        _logger.LogWarning("ResolveProjectFileUrl failed to resolve path='{Path}', context='{Context}'", path, contextResource);
        return string.Empty;
    }

    public void RegisterAssetsFolder(string folderPath)
    {
        var provider = CreateFileProvider("assets", folderPath);
        if (provider is null)
        {
            return;
        }

        _assetsFileProvider?.Dispose();
        _assetsFileProvider = provider;
        _logger.LogDebug("Registered WebView assets folder -> {FolderPath}", folderPath);
    }

    public void RegisterPackageFolder(string packageName, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return;
        }

        var provider = CreateFileProvider(packageName, folderPath);
        if (provider is null)
        {
            return;
        }

        var previousProvider = _packageFileProviders.TryGetValue(packageName, out var existingProvider)
            ? existingProvider
            : null;
        _packageFileProviders[packageName] = provider;
        previousProvider?.Dispose();

        _logger.LogDebug("Registered WebView package '{PackageName}' -> {FolderPath}", packageName, folderPath);
    }

    public void UnregisterPackageFolder(string packageName)
    {
        if (_packageFileProviders.TryRemove(packageName, out var provider))
        {
            provider.Dispose();
        }
    }

    public void RegisterCrossOriginReader(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return;
        }

        _crossOriginReaders[origin] = 0;
        _logger.LogDebug("Registered cross-origin reader -> {Origin}", origin);
    }

    public string GetProjectUrl(string path)
    {
        return BuildContentUrl("project", path);
    }

    public string GetAssetsUrl(string path)
    {
        return BuildContentUrl("assets", path);
    }

    public string GetPackageUrl(string packageName, string path)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return string.Empty;
        }

        return BuildContentUrl($"package/{packageName}", path);
    }

    private string BuildContentUrl(string area, string path)
    {
        if (_port == 0)
        {
            return string.Empty;
        }

        var normalizedPath = NormalizePath(path ?? string.Empty);

        return $"http://127.0.0.1:{_port}/{area}/{normalizedPath}";
    }

    private PhysicalFileProvider? CreateFileProvider(string label, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return null;
        }

        try
        {
            return new PhysicalFileProvider(folderPath);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to register WebView content folder '{Label}' at {FolderPath}", label, folderPath);
            return null;
        }
    }

    /// <summary>
    /// Combines a base folder path with a relative path and normalizes
    /// "." and ".." segments. Returns empty string if the result would
    /// escape above the project root.
    /// </summary>
    private static string CombineAndNormalize(string baseFolderPath, string relativePath)
    {
        var combined = string.IsNullOrEmpty(baseFolderPath)
            ? relativePath
            : $"{baseFolderPath}/{relativePath}";

        return NormalizePath(combined);
    }

    /// <summary>
    /// Normalizes a path by resolving "." and ".." segments.
    /// Returns empty string if the path would escape above the root.
    /// </summary>
    private static string NormalizePath(string path)
    {
        var segments = path.Split('/');
        var stack = new Stack<string>();

        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                if (stack.Count == 0)
                {
                    return string.Empty;
                }
                stack.Pop();
            }
            else if (segment != "." && !string.IsNullOrEmpty(segment))
            {
                stack.Push(segment);
            }
        }

        return string.Join("/", stack.Reverse());
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing)
            {
                _projectFileProvider?.Dispose();
                _projectFileProvider = null;

                _assetsFileProvider?.Dispose();
                _assetsFileProvider = null;

                foreach (var packageFileProvider in _packageFileProviders.Values)
                {
                    packageFileProvider.Dispose();
                }
                _packageFileProviders.Clear();
            }
        }
    }
}
