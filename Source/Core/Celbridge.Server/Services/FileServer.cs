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
            await context.Response.SendFileAsync(fileInfo);
        });
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
            _logger.LogDebug(
                "ResolveProjectFileUrl relative: contextFolder='{ContextFolder}', combined='{RelativePath}'",
                contextFolder, relativePath);

            if (!string.IsNullOrEmpty(relativePath))
            {
                var fileInfo = _projectFileProvider.GetFileInfo(relativePath);
                _logger.LogDebug(
                    "ResolveProjectFileUrl relative check: exists={Exists}, isDir={IsDir}",
                    fileInfo.Exists, fileInfo.IsDirectory);

                if (fileInfo.Exists && !fileInfo.IsDirectory)
                {
                    var resolvedUrl = $"http://127.0.0.1:{_port}/local/{relativePath}";
                    _logger.LogDebug("ResolveProjectFileUrl resolved (relative): {Url}", resolvedUrl);
                    return resolvedUrl;
                }
            }
        }

        // Try as an absolute resource key
        var normalizedPath = NormalizePath(path);
        _logger.LogDebug("ResolveProjectFileUrl absolute: normalizedPath='{NormalizedPath}'", normalizedPath);

        if (!string.IsNullOrEmpty(normalizedPath))
        {
            var fileInfo = _projectFileProvider.GetFileInfo(normalizedPath);
            _logger.LogDebug(
                "ResolveProjectFileUrl absolute check: exists={Exists}, isDir={IsDir}",
                fileInfo.Exists, fileInfo.IsDirectory);

            if (fileInfo.Exists && !fileInfo.IsDirectory)
            {
                var resolvedUrl = $"http://127.0.0.1:{_port}/local/{normalizedPath}";
                _logger.LogDebug("ResolveProjectFileUrl resolved (absolute): {Url}", resolvedUrl);
                return resolvedUrl;
            }
        }

        _logger.LogDebug("ResolveProjectFileUrl failed to resolve path='{Path}', context='{Context}'", path, contextResource);
        return string.Empty;
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
            }
        }
    }
}
