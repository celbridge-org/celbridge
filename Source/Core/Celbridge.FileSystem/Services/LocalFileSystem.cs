using System.Text;
using Celbridge.Resources;

namespace Celbridge.FileSystem.Services;

/// <summary>
/// v1 implementation of <see cref="IFileSystem"/>. A thin pass-through to the
/// System.IO static facades with a uniform bounded-retry policy applied to
/// every transient IOException (sharing violations from antivirus, indexers,
/// cloud-sync clients). The only direct-System.IO call site in product code;
/// all other consumers go through this layer.
/// </summary>
[AllowDirectFileSystemAccess]
public sealed class LocalFileSystem : IFileSystem
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

    public Task<Result<IReadOnlyList<string>>> EnumerateFilesAsync(string path, string pattern, bool recursive, Func<string, bool>? validateEntry = null)
    {
        try
        {
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var results = new List<string>();
            foreach (var entry in Directory.EnumerateFiles(path, pattern, searchOption))
            {
                if (validateEntry is not null
                    && !validateEntry(entry))
                {
                    continue;
                }
                results.Add(entry);
            }

            IReadOnlyList<string> list = results;
            return Task.FromResult(Result<IReadOnlyList<string>>.Ok(list));
        }
        catch (Exception ex)
        {
            var failure = Result.Fail($"Failed to enumerate files at: '{path}'")
                .WithException(ex);
            return Task.FromResult<Result<IReadOnlyList<string>>>(failure);
        }
    }

    public Task<Result<IReadOnlyList<string>>> EnumerateFoldersAsync(string path)
    {
        try
        {
            var folders = Directory.EnumerateDirectories(path).ToList();
            IReadOnlyList<string> list = folders;
            return Task.FromResult(Result<IReadOnlyList<string>>.Ok(list));
        }
        catch (Exception ex)
        {
            var failure = Result.Fail($"Failed to enumerate folders at: '{path}'")
                .WithException(ex);
            return Task.FromResult<Result<IReadOnlyList<string>>>(failure);
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

            if ((mask & FileSystemAttributes.Hidden) != 0)
            {
                if (set)
                {
                    currentNative |= System.IO.FileAttributes.Hidden;
                }
                else
                {
                    currentNative &= ~System.IO.FileAttributes.Hidden;
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

        if ((native & System.IO.FileAttributes.Hidden) != 0)
        {
            portable |= FileSystemAttributes.Hidden;
        }

        return portable;
    }
}
