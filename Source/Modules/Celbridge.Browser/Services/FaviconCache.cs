using System.Security.Cryptography;
using System.Text;
using Celbridge.Browser.Helpers;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.WebHost;

namespace Celbridge.Browser.Services;

// Keeps the current project's tab icons in a folder of the project data folder. An icon's file is named for the icon's
// address, so every page naming that address reuses the file.
internal sealed class FaviconCache : IWebViewFaviconStore
{
    private const string FaviconFileExtension = ".favicon";

    private readonly ILogger<FaviconCache> _logger;
    private readonly IProjectService _projectService;
    private readonly ILocalFileSystem _fileSystem;

    public FaviconCache(
        ILogger<FaviconCache> logger,
        IProjectService projectService,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _projectService = projectService;
        _fileSystem = fileSystem;
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
    /// Returns the file name for an icon's address. Addresses that BrowserUrlHelper normalizes to one address share a
    /// name.
    /// </summary>
    internal static string? GetIconFileName(string iconAddress)
    {
        if (!BrowserUrlHelper.TryNormalize(iconAddress, out var normalizedUrl)
            || !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri));

        return $"{Convert.ToHexStringLower(hash)}{FaviconFileExtension}";
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
