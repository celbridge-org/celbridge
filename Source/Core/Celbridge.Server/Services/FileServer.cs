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

    // Folders served under a ".celbridge" host name over loopback. This is the macOS replacement for
    // SetVirtualHostNameToFolderMapping, keyed by host name (e.g. "shared.celbridge").
    private readonly ConcurrentDictionary<string, PhysicalFileProvider> _hostFileProviders = new();

    // Per-session token required on the host routes so other local processes cannot read served
    // content over the loopback socket.
    private readonly string _hostAccessToken = Guid.NewGuid().ToString("N");

    private PhysicalFileProvider? _projectFileProvider;
    private int _port;
    private bool _disposed;

    public bool IsReady => _port != 0 && _projectFileProvider is not null;

    public string HostAccessToken => _hostAccessToken;

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

        // Serves files registered under a ".celbridge" host name. On the macOS Skia head WebView
        // documents are loaded under a synthetic "http://<host>.celbridge/" origin via native
        // loadHTMLString:baseURL:, and their assets are fetched cross-origin from here. The token
        // gates access so other local processes cannot read served content.
        application.MapGet("/host/{host}/{**path}", async (HttpContext context, string host, string path) =>
        {
            if (!IsAuthorizedHostRequest(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (!_hostFileProviders.TryGetValue(host, out var hostFileProvider))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var fileInfo = hostFileProvider.GetFileInfo(path);
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
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Cross-Origin-Embedder-Policy"] = "credentialless";
            context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";

            await context.Response.SendFileAsync(fileInfo);
        });
    }

    private bool IsAuthorizedHostRequest(HttpContext context)
    {
        var token = context.Request.Query["token"].ToString();
        return !string.IsNullOrEmpty(token)
            && string.Equals(token, _hostAccessToken, StringComparison.Ordinal);
    }

    /// <summary>
    /// Enables file serving for the given project folder and port.
    /// </summary>
    public void Enable(string projectFolderPath, int port)
    {
        _projectFileProvider?.Dispose();
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

    public void RegisterHostFolder(string hostName, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(hostName)
            || string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        PhysicalFileProvider provider;
        try
        {
            provider = new PhysicalFileProvider(folderPath);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to register host folder '{HostName}' at {FolderPath}", hostName, folderPath);
            return;
        }

        var previousProvider = _hostFileProviders.TryGetValue(hostName, out var existingProvider)
            ? existingProvider
            : null;
        _hostFileProviders[hostName] = provider;
        previousProvider?.Dispose();

        _logger.LogDebug("Registered WebView host '{HostName}' -> {FolderPath}", hostName, folderPath);
    }

    public void UnregisterHostFolder(string hostName)
    {
        if (_hostFileProviders.TryRemove(hostName, out var provider))
        {
            provider.Dispose();
        }
    }

    public string ResolveHostFileUrl(string hostName, string path)
    {
        if (_port == 0
            || string.IsNullOrWhiteSpace(hostName)
            || !_hostFileProviders.ContainsKey(hostName))
        {
            return string.Empty;
        }

        var normalizedPath = NormalizePath(path ?? string.Empty);

        return $"http://127.0.0.1:{_port}/host/{hostName}/{normalizedPath}?token={_hostAccessToken}";
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

                foreach (var hostFileProvider in _hostFileProviders.Values)
                {
                    hostFileProvider.Dispose();
                }
                _hostFileProviders.Clear();
            }
        }
    }
}
