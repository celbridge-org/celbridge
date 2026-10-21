using System.Collections.Concurrent;
using Celbridge.Resources;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Celbridge.Server.Services;

/// <summary>
/// Serves project files, shared web assets and package folders over HTTP on localhost via the server's
/// Kestrel instance. Project files are served at /project/{path}. A request for a project file is checked
/// by resource key before the file is served. So is a request for a file in a package folder inside the
/// project.
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

    private IRootHandlerRegistry? _rootHandlers;
    private IResourcePolicy? _policy;
    private PhysicalFileProvider? _projectFileProvider;
    private int _port;
    private bool _disposed;

    public bool IsReady => _port != 0 && _projectFileProvider is not null;

    public FileServer(ILogger<FileServer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers the /project/, /assets/ and /package/ routes on the given WebApplication.
    /// Must be called during Kestrel setup before the server starts.
    /// </summary>
    public void ConfigureEndpoints(WebApplication application)
    {
        // The three WebView content routes. WebViews are navigated to a loopback URL under one of
        // these and reference everything else root-relative, so the page resolves all content against
        // its own loopback origin.

        // /project/ serves the open project's files. Its pages fetch their own content same-origin, so it
        // opts out of cross-origin reads: a page in another local origin (e.g. one loaded in the user's
        // browser that discovered the loopback port) cannot read the project's files across origins.
        // Project files change with every save, agent write and external edit, so the route forbids caching
        // and a reloaded page always gets the current files.
        application.MapGet("/project/{**path}", (HttpContext context, string path) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            IFileInfo? fileInfo = null;
            if (IsServable(ResourceKey.Empty, path))
            {
                fileInfo = _projectFileProvider?.GetFileInfo(path);
            }

            return ServeFileAsync(context, fileInfo, path, allowCrossOrigin: false);
        });

        // /assets/ and /package/ serve bundled shared assets and package folders. The synthetic-origin
        // editor (a faked origin for a domain-locked library) pulls its lib and the shared client from
        // these routes cross-origin, so they opt in to cross-origin reads.
        application.MapGet("/assets/{**path}", (HttpContext context, string path) =>
            ServeFileAsync(context, _assetsFileProvider?.GetFileInfo(path), path, allowCrossOrigin: true));

        application.MapGet("/package/{name}/{**path}", (HttpContext context, string name, string path) =>
        {
            var fileInfo = ResolvePackageFile(name, path);
            return ServeFileAsync(context, fileInfo, path, allowCrossOrigin: true);
        });
    }

    // A package folder inside a resource root is checked by key, like any project file. A bundled package's
    // folder belongs to the application, and the package's file provider serves the folder directly.
    internal IFileInfo? ResolvePackageFile(string packageName, string path)
    {
        if (!_packageFileProviders.TryGetValue(packageName, out var packageFileProvider))
        {
            return null;
        }

        var rootHandlers = _rootHandlers;
        if (rootHandlers is not null)
        {
            var folderKeyResult = rootHandlers.GetResourceKey(packageFileProvider.Root);
            if (folderKeyResult.IsSuccess
                && !IsServable(folderKeyResult.Value, path))
            {
                return null;
            }
        }

        return packageFileProvider.GetFileInfo(path);
    }

    private bool IsServable(ResourceKey folder, string path)
    {
        var rootHandlers = _rootHandlers;
        var policy = _policy;
        if (rootHandlers is null
            || policy is null)
        {
            return false;
        }

        var checkResult = CheckServedFile(rootHandlers, policy, folder, path);
        return checkResult.IsSuccess;
    }

    /// <summary>
    /// Checks that a URL path under a folder key may be served. The path becomes a resource key. The policy
    /// check refuses a reserved folder, like .git. The root's resolver refuses a link and a path through a link.
    /// The resolver also refuses a path that does not match the names stored on disk.
    /// </summary>
    internal static Result CheckServedFile(
        IRootHandlerRegistry rootHandlers,
        IResourcePolicy policy,
        ResourceKey folder,
        string path)
    {
        var keyString = string.IsNullOrEmpty(folder.Path)
            ? $"{folder.Root}:{path}"
            : $"{folder.FullKey}/{path}";

        if (!ResourceKey.TryCreate(keyString, out var resource))
        {
            return Result.Fail($"The path does not name a resource: '{path}'");
        }

        var policyResult = policy.Evaluate(resource, ResourceAction.Read);
        if (policyResult.IsFailure)
        {
            return policyResult;
        }

        var resolveResult = rootHandlers.ResolveResourcePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail(resolveResult);
        }

        return Result.Ok();
    }

    private async Task ServeFileAsync(HttpContext context, IFileInfo? fileInfo, string path, bool allowCrossOrigin)
    {
        if (fileInfo is null
            || !fileInfo.Exists
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

        // These enable WebContainer support in local HTML/JS.
        context.Response.Headers["Cross-Origin-Embedder-Policy"] = "credentialless";
        context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";

        await context.Response.SendFileAsync(fileInfo);
    }

    public void Enable(IRootHandlerRegistry rootHandlers, IResourcePolicy policy, int port)
    {
        var resolveResult = rootHandlers.ResolveResourcePath(ResourceKey.Empty);
        if (resolveResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to resolve the project folder: {resolveResult.MessageChain}");
        }
        var projectFolderPath = resolveResult.Value;

        _projectFileProvider?.Dispose();
        _projectFileProvider = new PhysicalFileProvider(projectFolderPath);
        _rootHandlers = rootHandlers;
        _policy = policy;
        _port = port;
        _logger.LogInformation("Project file serving enabled for {FolderPath}", projectFolderPath);
    }

    public void Disable()
    {
        _rootHandlers = null;
        _policy = null;
        _projectFileProvider?.Dispose();
        _projectFileProvider = null;
        _port = 0;

        // A package folder registered for the closed workspace would be judged against the next workspace's
        // roots. Editors register their package folder again when they load.
        UnregisterAllPackageFolders();
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

    private void UnregisterAllPackageFolders()
    {
        foreach (var packageName in _packageFileProviders.Keys)
        {
            UnregisterPackageFolder(packageName);
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
                _rootHandlers = null;
                _policy = null;
                _projectFileProvider?.Dispose();
                _projectFileProvider = null;

                _assetsFileProvider?.Dispose();
                _assetsFileProvider = null;

                UnregisterAllPackageFolders();
            }
        }
    }
}
