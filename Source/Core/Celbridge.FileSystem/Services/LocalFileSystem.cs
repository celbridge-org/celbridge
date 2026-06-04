using System.Text;

namespace Celbridge.FileSystem.Services;

/// <summary>
/// v1 implementation of <see cref="ILocalFileSystem"/>. A thin pass-through to the
/// System.IO static facades with a uniform bounded-retry policy applied to
/// every transient IOException (sharing violations from antivirus, indexers,
/// cloud-sync clients). The only direct-System.IO call site in product code;
/// all other consumers go through this layer.
/// </summary>
public sealed class LocalFileSystem : ILocalFileSystem
{
    private const int StreamBufferSize = 4096;

    private readonly ILogger<LocalFileSystem> _logger;

    public LocalFileSystem(ILogger<LocalFileSystem> logger)
    {
        _logger = logger;
    }

    public Task<Result<byte[]>> ReadAllBytesAsync(string path)
    {
        return RetryPolicy.RunAsync(
            _logger,
            operationLabel: "Read",
            path: path,
            operation: () => File.ReadAllBytesAsync(path),
            shouldRetry: IsTransientReadIOException);
    }

    public Task<Result<string>> ReadAllTextAsync(string path)
    {
        return RetryPolicy.RunAsync(
            _logger,
            operationLabel: "Read",
            path: path,
            operation: () => File.ReadAllTextAsync(path),
            shouldRetry: IsTransientReadIOException);
    }

    public Task<Result<Stream>> OpenReadAsync(string path)
    {
        return RetryPolicy.RunAsync(
            _logger,
            operationLabel: "Read",
            path: path,
            operation: () => Task.FromResult<Stream>(new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                StreamBufferSize,
                useAsync: true)),
            shouldRetry: IsTransientReadIOException);
    }

    public Task<Result<Stream>> OpenWriteAsync(string path, WriteMode mode)
    {
        var fileMode = mode switch
        {
            WriteMode.Truncate => FileMode.Create,
            WriteMode.Append => FileMode.Append,
            WriteMode.CreateNew => FileMode.CreateNew,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown WriteMode."),
        };

        // Retry covers only the open. Writes through the returned stream are out
        // of our reach; a partial write on crash leaves the file truncated, just
        // as it does for WriteAllBytesAsync.
        return RetryPolicy.RunAsync(
            _logger,
            operationLabel: "Write",
            path: path,
            operation: () => Task.FromResult<Stream>(new FileStream(
                path,
                fileMode,
                FileAccess.Write,
                FileShare.None,
                StreamBufferSize,
                useAsync: true)));
    }

    public async Task<Result> WriteAllBytesAsync(string path, byte[] bytes)
    {
        var runResult = await RetryPolicy.RunAsync<bool>(
            _logger,
            operationLabel: "Write",
            path: path,
            operation: async () =>
            {
                await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);

        return runResult.IsSuccess ? Result.Ok() : Result.Fail(runResult);
    }

    public async Task<Result> WriteAllTextAsync(string path, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return await WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
    }

    public async Task<Result<StorageItemInfo>> GetInfoAsync(string path)
    {
        await Task.CompletedTask;

        try
        {
            // FileInfo.Exists, .Length, .LastWriteTimeUtc, and .Attributes share
            // the same underlying stat() call; populating the rich record costs
            // no more than a plain existence probe.
            var fileInfo = new FileInfo(path);
            if (fileInfo.Exists)
            {
                var fileAttributes = MapToPortable(fileInfo.Attributes);
                var fileResult = new StorageItemInfo(
                    Kind: StorageItemKind.File,
                    Size: fileInfo.Length,
                    ModifiedUtc: fileInfo.LastWriteTimeUtc,
                    Attributes: fileAttributes);
                return fileResult;
            }

            var directoryInfo = new DirectoryInfo(path);
            if (directoryInfo.Exists)
            {
                var folderAttributes = MapToPortable(directoryInfo.Attributes);
                var folderResult = new StorageItemInfo(
                    Kind: StorageItemKind.Folder,
                    Size: 0,
                    ModifiedUtc: directoryInfo.LastWriteTimeUtc,
                    Attributes: folderAttributes);
                return folderResult;
            }

            var notFoundResult = new StorageItemInfo(
                Kind: StorageItemKind.NotFound,
                Size: 0,
                ModifiedUtc: default,
                Attributes: FileSystemAttributes.None);
            return notFoundResult;
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to get info for path: '{path}'")
                .WithException(ex);
        }
    }

    public async Task<Result<IReadOnlyList<FileSystemEntry>>> EnumerateAsync(string path, string pattern, bool recursive)
    {
        await Task.CompletedTask;

        try
        {
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var entries = new List<FileSystemEntry>();

            // Enumerating through DirectoryInfo yields FileInfo / DirectoryInfo
            // objects whose Length and LastWriteTimeUtc are populated from the
            // directory walk itself, so size and modified-time cost no extra stat
            // per item. EnumerateDirectories and EnumerateFiles each report a
            // single kind. The OS enumeration order is unspecified (NTFS comes
            // back name-sorted, ext4 hash-arbitrary), so each group is sorted by
            // ordinal path to give a deterministic, cross-platform-stable result
            // for every consumer.
            var directoryInfo = new DirectoryInfo(path);

            var folderInfos = directoryInfo.EnumerateDirectories(pattern, searchOption).OrderBy(folder => folder.FullName, StringComparer.Ordinal);
            foreach (var folderInfo in folderInfos)
            {
                entries.Add(new FileSystemEntry(folderInfo.FullName, IsFolder: true, Size: 0, ModifiedUtc: folderInfo.LastWriteTimeUtc));
            }

            var fileInfos = directoryInfo.EnumerateFiles(pattern, searchOption).OrderBy(file => file.FullName, StringComparer.Ordinal);
            foreach (var fileInfo in fileInfos)
            {
                entries.Add(new FileSystemEntry(fileInfo.FullName, IsFolder: false, Size: fileInfo.Length, ModifiedUtc: fileInfo.LastWriteTimeUtc));
            }

            IReadOnlyList<FileSystemEntry> list = entries;
            return list.OkResult();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to enumerate entries at: '{path}'")
                .WithException(ex);
        }
    }

    public async Task<Result> MoveFileAsync(string source, string dest)
    {
        var runResult = await RetryPolicy.RunAsync<bool>(
            _logger,
            operationLabel: "Move",
            path: source,
            operation: () =>
            {
                File.Move(source, dest);
                return Task.FromResult(true);
            }).ConfigureAwait(false);

        return runResult.IsSuccess ? Result.Ok() : Result.Fail(runResult);
    }

    public async Task<Result> MoveFolderAsync(string source, string dest)
    {
        var runResult = await RetryPolicy.RunAsync<bool>(
            _logger,
            operationLabel: "Move",
            path: source,
            operation: () =>
            {
                Directory.Move(source, dest);
                return Task.FromResult(true);
            }).ConfigureAwait(false);

        return runResult.IsSuccess ? Result.Ok() : Result.Fail(runResult);
    }

    public async Task<Result> CopyFileAsync(string source, string dest)
    {
        var runResult = await RetryPolicy.RunAsync<bool>(
            _logger,
            operationLabel: "Copy",
            path: source,
            operation: () =>
            {
                File.Copy(source, dest);
                return Task.FromResult(true);
            }).ConfigureAwait(false);

        return runResult.IsSuccess ? Result.Ok() : Result.Fail(runResult);
    }

    public async Task<Result> DeleteFileAsync(string path)
    {
        var runResult = await RetryPolicy.RunAsync<bool>(
            _logger,
            operationLabel: "Delete",
            path: path,
            operation: () =>
            {
                File.Delete(path);
                return Task.FromResult(true);
            }).ConfigureAwait(false);

        return runResult.IsSuccess ? Result.Ok() : Result.Fail(runResult);
    }

    public async Task<Result> DeleteFolderAsync(string path, bool recursive)
    {
        var runResult = await RetryPolicy.RunAsync<bool>(
            _logger,
            operationLabel: "Delete",
            path: path,
            operation: () =>
            {
                Directory.Delete(path, recursive);
                return Task.FromResult(true);
            }).ConfigureAwait(false);

        return runResult.IsSuccess ? Result.Ok() : Result.Fail(runResult);
    }

    public async Task<Result> CreateFolderAsync(string path)
    {
        await Task.CompletedTask;

        try
        {
            // Directory.CreateDirectory is idempotent: existing folders return
            // the DirectoryInfo without error, and missing intermediate parents
            // are created in the same call.
            Directory.CreateDirectory(path);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to create folder: '{path}'")
                .WithException(ex);
        }
    }

    public async Task<Result> SetAttributesAsync(string path, FileSystemAttributes mask, bool set)
    {
        await Task.CompletedTask;

        try
        {
            // Read-modify-write on the DOS attribute bitfield. Only the bits
            // named in mask are touched; everything else is preserved.
            var currentNative = File.GetAttributes(path);

            if ((mask & FileSystemAttributes.ReadOnly) != 0)
            {
                if (set)
                {
                    currentNative |= System.IO.FileAttributes.ReadOnly;
                }
                else
                {
                    currentNative &= ~System.IO.FileAttributes.ReadOnly;
                }
            }

            File.SetAttributes(path, currentNative);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to set attributes on path: '{path}'")
                .WithException(ex);
        }
    }

    // Reads short-circuit FileNotFoundException and DirectoryNotFoundException
    // so a genuinely missing file fails immediately rather than burning the
    // retry budget on a hopeless case.
    private static bool IsTransientReadIOException(IOException ex)
    {
        return ex is not FileNotFoundException
            and not DirectoryNotFoundException;
    }

    private static FileSystemAttributes MapToPortable(System.IO.FileAttributes native)
    {
        var portable = FileSystemAttributes.None;

        if ((native & System.IO.FileAttributes.ReadOnly) != 0)
        {
            portable |= FileSystemAttributes.ReadOnly;
        }

        return portable;
    }
}
