using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Celbridge.Logging;

namespace Celbridge.Dependencies.Services;

/// <summary>
/// Downloads a dependency's archive into a temporary folder beside the dependency's folder, checks the archive's
/// hash, and extracts it there. A rename then gives the extracted folder its final name, so a folder with the final
/// name is always complete. The rename stays on one volume, because the temporary folder shares the parent folder.
/// </summary>
internal sealed class DependencyDownloader : IDependencyDownloader
{
    private const string TemporaryFolderPrefix = ".download-";
    private const string ContentFolderName = "content";
    private const string ZipExtension = ".zip";
    private const string TarGzExtension = ".tar.gz";
    private const string TgzExtension = ".tgz";

    // Covers the request and every read of the response. A slow connection takes minutes over an archive of tens of
    // megabytes.
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger<DependencyDownloader> _logger;
    private readonly HttpClient _httpClient;

    public DependencyDownloader(
        ILocalFileSystem fileSystem,
        ILogger<DependencyDownloader> logger)
        : this(fileSystem, logger, new HttpClientHandler())
    {
    }

    internal DependencyDownloader(
        ILocalFileSystem fileSystem,
        ILogger<DependencyDownloader> logger,
        HttpMessageHandler messageHandler)
    {
        _fileSystem = fileSystem;
        _logger = logger;
        _httpClient = new HttpClient(messageHandler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task<Result> DownloadAsync(DependencyDownload download)
    {
        var archiveExtension = GetArchiveExtension(download.Url);
        if (string.IsNullOrEmpty(archiveExtension))
        {
            return Result.Fail($"The archive at '{download.Url}' is not a .zip, .tar.gz or .tgz archive");
        }

        var folderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(download.FolderPath));
        var parentFolderPath = Path.GetDirectoryName(folderPath);
        if (string.IsNullOrEmpty(parentFolderPath))
        {
            return Result.Fail($"The dependency folder '{folderPath}' has no parent folder");
        }

        var temporaryFolderPath = Path.Combine(parentFolderPath, $"{TemporaryFolderPrefix}{Guid.NewGuid():N}");
        try
        {
            var downloadResult = await DownloadIntoAsync(download, archiveExtension, folderPath, temporaryFolderPath);
            if (downloadResult.IsFailure)
            {
                return Result.Fail($"Failed to download the archive at '{download.Url}'")
                    .WithErrors(downloadResult);
            }

            return Result.Ok();
        }
        catch (Exception exception)
        {
            return Result.Fail($"Failed to download the archive at '{download.Url}'")
                .WithException(exception);
        }
        finally
        {
            await DeleteTemporaryFolderAsync(temporaryFolderPath);
        }
    }

    private async Task<Result> DownloadIntoAsync(
        DependencyDownload download,
        string archiveExtension,
        string folderPath,
        string temporaryFolderPath)
    {
        var createResult = await _fileSystem.CreateFolderAsync(temporaryFolderPath);
        if (createResult.IsFailure)
        {
            return createResult;
        }

        var archivePath = Path.Combine(temporaryFolderPath, $"archive{archiveExtension}");
        var expectedHash = download.Hash;
        var fetchResult = await FetchArchiveAsync(download.Url, archivePath, expectedHash.Algorithm);
        if (fetchResult.IsFailure)
        {
            return Result.Fail(fetchResult);
        }
        var archiveHash = fetchResult.Value;

        if (!string.Equals(archiveHash, expectedHash.Value, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail($"The archive's {expectedHash.Algorithm} hash is '{archiveHash}', and the expected hash is '{expectedHash.Value}'");
        }

        var contentFolderPath = Path.Combine(temporaryFolderPath, ContentFolderName);
        var extractResult = await ExtractArchiveAsync(archivePath, archiveExtension, contentFolderPath);
        if (extractResult.IsFailure)
        {
            return extractResult;
        }

        var extractedFolderPath = contentFolderPath;
        if (!string.IsNullOrEmpty(download.ArchiveFolder))
        {
            extractedFolderPath = Path.Combine(contentFolderPath, download.ArchiveFolder);
        }

        if (await _fileSystem.GetKindAsync(extractedFolderPath) != StorageItemKind.Folder)
        {
            return Result.Fail($"The archive contains no folder named '{download.ArchiveFolder}'");
        }

        var moveResult = await _fileSystem.MoveFolderAsync(extractedFolderPath, folderPath);
        if (moveResult.IsSuccess)
        {
            _logger.LogInformation("Downloaded '{Url}' into '{Folder}'", download.Url, folderPath);
            return Result.Ok();
        }

        // Another copy of Celbridge that downloads the same dependency at the same time may rename its folder first.
        // Its folder is complete, so this download has nothing left to do.
        if (await _fileSystem.GetKindAsync(folderPath) == StorageItemKind.Folder)
        {
            _logger.LogInformation("Found '{Folder}' in place after downloading '{Url}'", folderPath, download.Url);
            return Result.Ok();
        }

        return Result.Fail($"Failed to rename the extracted folder to '{folderPath}'")
            .WithErrors(moveResult);
    }

    // Writes the archive to the path, and returns the archive's hash as lowercase hex.
    private async Task<Result<string>> FetchArchiveAsync(
        string url,
        string archivePath,
        DependencyHashAlgorithm hashAlgorithm)
    {
        using var timeoutSource = new CancellationTokenSource(DownloadTimeout);
        var cancellationToken = timeoutSource.Token;

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result<string>.Fail($"The server answered with status {(int)response.StatusCode} ({response.StatusCode})");
        }

        var openResult = await _fileSystem.OpenWriteAsync(archivePath, WriteMode.Truncate);
        if (openResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to create the archive file '{archivePath}'")
                .WithErrors(openResult);
        }

        var hashAlgorithmName = HashAlgorithmName.SHA256;
        if (hashAlgorithm == DependencyHashAlgorithm.Sha512)
        {
            hashAlgorithmName = HashAlgorithmName.SHA512;
        }

        using var hash = IncrementalHash.CreateHash(hashAlgorithmName);
        await using (var archiveStream = openResult.Value)
        await using (var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            var buffer = new byte[81920];
            while (true)
            {
                var readCount = await responseStream.ReadAsync(buffer, cancellationToken);
                if (readCount == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, readCount);
                await archiveStream.WriteAsync(buffer.AsMemory(0, readCount), cancellationToken);
            }
        }

        var hashBytes = hash.GetHashAndReset();
        var hashText = Convert.ToHexStringLower(hashBytes);

        return hashText;
    }

    // TarFile keeps each entry's Unix mode, so an extracted executable stays executable. Both extractors refuse an
    // entry with a path outside the destination folder.
    private async Task<Result> ExtractArchiveAsync(string archivePath, string archiveExtension, string contentFolderPath)
    {
        var createResult = await _fileSystem.CreateFolderAsync(contentFolderPath);
        if (createResult.IsFailure)
        {
            return createResult;
        }

        var openResult = await _fileSystem.OpenReadAsync(archivePath);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to open the archive file '{archivePath}'")
                .WithErrors(openResult);
        }

        // Decompression keeps a thread busy, so it runs off the UI thread.
        try
        {
            await Task.Run(async () =>
            {
                await using var archiveStream = openResult.Value;
                if (archiveExtension == ZipExtension)
                {
                    await ZipFile.ExtractToDirectoryAsync(archiveStream, contentFolderPath, overwriteFiles: false);
                }
                else
                {
                    await using var gzipStream = new GZipStream(archiveStream, CompressionMode.Decompress);
                    await TarFile.ExtractToDirectoryAsync(gzipStream, contentFolderPath, overwriteFiles: false);
                }
            });
        }
        catch (Exception exception)
        {
            return Result.Fail($"Failed to extract the archive file '{archivePath}'")
                .WithException(exception);
        }

        return Result.Ok();
    }

    private async Task DeleteTemporaryFolderAsync(string temporaryFolderPath)
    {
        if (await _fileSystem.GetKindAsync(temporaryFolderPath) != StorageItemKind.Folder)
        {
            return;
        }

        var deleteResult = await _fileSystem.DeleteFolderAsync(temporaryFolderPath, recursive: true);
        if (deleteResult.IsFailure)
        {
            _logger.LogWarning(deleteResult, $"Failed to delete the temporary download folder '{temporaryFolderPath}'");
        }
    }

    private static string GetArchiveExtension(string url)
    {
        var path = url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            path = uri.AbsolutePath;
        }

        if (path.EndsWith(ZipExtension, StringComparison.OrdinalIgnoreCase))
        {
            return ZipExtension;
        }

        // A .tgz archive is a .tar.gz archive under a shorter name.
        if (path.EndsWith(TarGzExtension, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(TgzExtension, StringComparison.OrdinalIgnoreCase))
        {
            return TarGzExtension;
        }

        return string.Empty;
    }
}
