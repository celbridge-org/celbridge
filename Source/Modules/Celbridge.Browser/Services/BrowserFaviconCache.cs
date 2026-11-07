using System.Security.Cryptography;
using System.Text;
using Celbridge.Browser.Helpers;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.WebHost;

namespace Celbridge.Browser.Services;

/// <summary>
/// Stores favicons for the current project: the icon from an icon address, and the icon of a bookmark's page, by the
/// bookmark's address.
/// </summary>
public interface IBrowserFaviconCache : IWebViewFaviconStore
{
    /// <summary>
    /// Returns the cached icon for a bookmark's address, or null when the cache has none.
    /// </summary>
    Task<byte[]?> TryReadBookmarkIconAsync(string url);

    /// <summary>
    /// Stores the icon for a bookmark's address.
    /// </summary>
    Task WriteBookmarkIconAsync(string url, byte[] favicon);
}

// Keeps favicons in a folder of the project data folder. An icon's file is named for the icon's address, so every
// page naming that address reuses the file. A bookmark's file is named for the bookmark's address, so
// documents with the same bookmark address share a file.
internal sealed class BrowserFaviconCache : IBrowserFaviconCache
{
    private const string FaviconFileExtension = ".favicon";
    private const string BookmarkFilePrefix = "bookmark-";
    private const string IconFilePrefix = "icon-";

    private readonly ILogger<BrowserFaviconCache> _logger;
    private readonly IProjectService _projectService;
    private readonly ILocalFileSystem _fileSystem;

    public BrowserFaviconCache(
        ILogger<BrowserFaviconCache> logger,
        IProjectService projectService,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _projectService = projectService;
        _fileSystem = fileSystem;
    }

    public Task<byte[]?> TryReadBookmarkIconAsync(string url)
    {
        return TryReadAsync(GetBookmarkFileName(url), url);
    }

    public Task WriteBookmarkIconAsync(string url, byte[] favicon)
    {
        return WriteAsync(GetBookmarkFileName(url), url, favicon);
    }

    public Task<byte[]?> TryReadIconAsync(string iconAddress)
    {
        return TryReadAsync(GetIconFileName(iconAddress), iconAddress);
    }

    public Task WriteIconAsync(string iconAddress, byte[] favicon)
    {
        return WriteAsync(GetIconFileName(iconAddress), iconAddress, favicon);
    }

    private async Task<byte[]?> TryReadAsync(string? fileName, string url)
    {
        var path = GetFilePath(fileName);
        if (path is null)
        {
            return null;
        }

        var infoResult = await _fileSystem.GetInfoAsync(path);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            return null;
        }

        var readResult = await _fileSystem.ReadAllBytesAsync(path);
        if (readResult.IsFailure)
        {
            _logger.LogDebug($"Failed to read the cached favicon for '{url}'");
            return null;
        }

        return readResult.Value;
    }

    private async Task WriteAsync(string? fileName, string url, byte[] favicon)
    {
        var path = GetFilePath(fileName);
        if (path is null)
        {
            return;
        }

        var folderResult = await _fileSystem.CreateFolderAsync(Path.GetDirectoryName(path)!);
        if (folderResult.IsFailure)
        {
            _logger.LogDebug($"Failed to create the favicon folder for '{url}'");
            return;
        }

        var writeResult = await _fileSystem.WriteAllBytesAsync(path, favicon);
        if (writeResult.IsFailure)
        {
            _logger.LogDebug($"Failed to cache the favicon for '{url}'");
        }
    }

    /// <summary>
    /// Returns the file name for a bookmark's address. Two forms of one address share a name.
    /// </summary>
    internal static string? GetBookmarkFileName(string url)
    {
        return GetFileName(BookmarkFilePrefix, url);
    }

    /// <summary>
    /// Returns the file name for an icon's address. A bookmark at the same address gets a different file, which contains
    /// the icon of the bookmark's page.
    /// </summary>
    internal static string? GetIconFileName(string iconAddress)
    {
        return GetFileName(IconFilePrefix, iconAddress);
    }

    private static string? GetFileName(string prefix, string url)
    {
        if (!BrowserUrlHelper.TryNormalize(url, out var normalizedUrl)
            || !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri));

        return $"{prefix}{Convert.ToHexStringLower(hash)}{FaviconFileExtension}";
    }

    private string? GetFilePath(string? fileName)
    {
        var project = _projectService.CurrentProject;
        if (project is null
            || fileName is null)
        {
            return null;
        }

        return Path.Combine(project.ProjectDataFolderPath, ProjectConstants.FaviconsFolder, fileName);
    }
}
