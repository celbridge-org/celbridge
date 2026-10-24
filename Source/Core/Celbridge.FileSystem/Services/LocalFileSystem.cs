using System.Runtime.ExceptionServices;
using System.Text;

namespace Celbridge.FileSystem.Services;

/// <summary>
/// Passes filesystem calls through to the System.IO facades, wrapping each in a
/// bounded retry that absorbs the transient sharing violations antivirus,
/// indexers, and cloud-sync clients cause.
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
                // Every property here answers for the link itself: a link's Length is the length of the
                // path it holds, and a link left behind by a deleted target still reports as a file. So a
                // link is answered by describing what it points at.
                if (LinkHelper.IsLink(fileInfo))
                {
                    var linkTargetPath = ResolveLinkTargetPath(fileInfo);
                    if (linkTargetPath is null)
                    {
                        var brokenLinkResult = new StorageItemInfo(
                            Kind: StorageItemKind.BrokenLink,
                            Size: 0,
                            ModifiedUtc: fileInfo.LastWriteTimeUtc,
                            Attributes: MapToPortable(fileInfo.Attributes),
                            IsLink: true);
                        return brokenLinkResult;
                    }

                    var targetInfoResult = await GetInfoAsync(linkTargetPath);
                    if (targetInfoResult.IsFailure)
                    {
                        return targetInfoResult;
                    }

                    // The target says what the caller can open. The flag says a link is how they reached it.
                    var linkedInfo = targetInfoResult.Value with
                    {
                        IsLink = true
                    };
                    return linkedInfo;
                }

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
                    Attributes: folderAttributes,
                    IsLink: LinkHelper.IsLink(directoryInfo));
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

            // Probed rather than left to the enumeration, which throws DirectoryNotFoundException.
            // Asking about a folder that does not exist yet is routine here — a project acquires its
            // reports, trash and package folders lazily — so it reports as a failed result rather than
            // as an exception the debugger breaks on once per call.
            if (!directoryInfo.Exists)
            {
                return Result<IReadOnlyList<FileSystemEntry>>.Fail($"Folder does not exist: '{path}'");
            }

            var folderInfos = new List<DirectoryInfo>();
            var fileInfos = new List<FileInfo>();
            CollectEntries(directoryInfo, pattern, recursive, folderInfos, fileInfos);

            // FileSystemInfo.Attributes is also populated from the directory walk,
            // so the portable read-only flag costs no extra stat per item.
            foreach (var folderInfo in folderInfos.OrderBy(folder => folder.FullName, StringComparer.Ordinal))
            {
                var folderEntry = await DescribeEntryAsync(
                    folderInfo, StorageItemKind.Folder, size: 0);
                entries.Add(folderEntry);
            }

            foreach (var fileInfo in fileInfos.OrderBy(file => file.FullName, StringComparer.Ordinal))
            {
                var fileEntry = await DescribeEntryAsync(
                    fileInfo, StorageItemKind.File, fileInfo.Length);
                entries.Add(fileEntry);
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

    public async Task<Result<bool>> HasEntryAsync(string path, Func<FileSystemEntry, bool> predicate)
    {
        try
        {
            var directoryInfo = new DirectoryInfo(path);

            // Probed so a missing folder fails without raising DirectoryNotFoundException.
            if (!directoryInfo.Exists)
            {
                return Result<bool>.Fail($"Folder does not exist: '{path}'");
            }

            // EnumerateFileSystemInfos reads each entry only when the loop reaches it. Returning at the first accepted
            // entry leaves the rest of the folder unread.
            foreach (var entryInfo in directoryInfo.EnumerateFileSystemInfos())
            {
                var entry = entryInfo is FileInfo fileInfo
                    ? await DescribeEntryAsync(fileInfo, StorageItemKind.File, fileInfo.Length)
                    : await DescribeEntryAsync(entryInfo, StorageItemKind.Folder, size: 0);

                if (predicate(entry))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to enumerate entries at: '{path}'")
                .WithException(ex);
        }
    }

    public async Task<Result> MoveFileAsync(string source, string dest, bool overwrite = false)
    {
        var runResult = await RetryPolicy.RunAsync<bool>(
            _logger,
            operationLabel: "Move",
            path: source,
            operation: () => RunOnWorkerThreadAsync(() => File.Move(source, dest, overwrite))).ConfigureAwait(false);

        return runResult.IsSuccess ? Result.Ok() : Result.Fail(runResult);
    }

    public async Task<Result> MoveFolderAsync(string source, string dest)
    {
        var runResult = await RetryPolicy.RunAsync<bool>(
            _logger,
            operationLabel: "Move",
            path: source,
            operation: () => RunOnWorkerThreadAsync(() => Directory.Move(source, dest)),
            shouldRetry: exception => !CrossVolumeFolderMove.IsCrossVolumeFailure(exception, source, dest)).ConfigureAwait(false);

        if (runResult.IsSuccess)
        {
            return Result.Ok();
        }

        var renameException = runResult.FirstException;
        if (renameException is null
            || !CrossVolumeFolderMove.IsCrossVolumeFailure(renameException, source, dest))
        {
            return Result.Fail(runResult);
        }

        // A rename can't move a folder to another volume, so copy it there instead. The copy returns its
        // failures as a Result and never throws.
        return await Task.Run(() => CrossVolumeFolderMove.Move(source, dest, _logger)).ConfigureAwait(false);
    }

    public async Task<Result> CopyFileAsync(string source, string dest)
    {
        var runResult = await RetryPolicy.RunAsync<bool>(
            _logger,
            operationLabel: "Copy",
            path: source,
            operation: () => RunOnWorkerThreadAsync(() => File.Copy(source, dest))).ConfigureAwait(false);

        return runResult.IsSuccess ? Result.Ok() : Result.Fail(runResult);
    }

    public async Task<Result> DeleteFileAsync(string path)
    {
        var runResult = await RetryPolicy.RunAsync<bool>(
            _logger,
            operationLabel: "Delete",
            path: path,
            operation: () => RunOnWorkerThreadAsync(() => File.Delete(path))).ConfigureAwait(false);

        return runResult.IsSuccess ? Result.Ok() : Result.Fail(runResult);
    }

    public async Task<Result> DeleteFolderAsync(string path, bool recursive)
    {
        var runResult = await RetryPolicy.RunAsync<bool>(
            _logger,
            operationLabel: "Delete",
            path: path,
            operation: () => RunOnWorkerThreadAsync(() => DeleteFolder(path, recursive))).ConfigureAwait(false);

        return runResult.IsSuccess ? Result.Ok() : Result.Fail(runResult);
    }

    // A recursive delete removes each link without following it.
    private static void DeleteFolder(string path, bool recursive)
    {
        if (recursive)
        {
            FolderTreeDeleter.Delete(path);
            return;
        }

        Directory.Delete(path, recursive: false);
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
            // Clearing an attribute on a path that does not exist yet is the normal case for a caller
            // preparing to create the file, so it succeeds rather than failing on the missing file.
            if (!File.Exists(path)
                && !Directory.Exists(path))
            {
                return Result.Ok();
            }

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

    // Deletes, moves and copies have no asynchronous form in System.IO, so they run on a worker thread
    // rather than on the caller's. The command queue calls them from the UI thread, where one that waits
    // on a file antivirus is still scanning would freeze the application until the scan ends.
    // The worker returns the operation's exception, and this method rethrows it. An exception escaping the
    // worker stops the debugger as user-unhandled, even though the caller catches it.
    private static async Task<bool> RunOnWorkerThreadAsync(Action operation)
    {
        var failure = await Task.Run<ExceptionDispatchInfo?>(() =>
        {
            try
            {
                operation();
                return null;
            }
            catch (Exception ex)
            {
                return ExceptionDispatchInfo.Capture(ex);
            }
        }).ConfigureAwait(false);

        failure?.Throw();

        return true;
    }

    // Collects the folders and files that match the pattern in this folder. A recursive walk then descends into
    // every subfolder except a link.
    private static void CollectEntries(
        DirectoryInfo folder, string pattern, bool recursive, List<DirectoryInfo> folderInfos, List<FileInfo> fileInfos)
    {
        var matchedFolders = folder.EnumerateDirectories(pattern, SearchOption.TopDirectoryOnly).ToList();
        folderInfos.AddRange(matchedFolders);
        fileInfos.AddRange(folder.EnumerateFiles(pattern, SearchOption.TopDirectoryOnly));

        if (!recursive)
        {
            return;
        }

        var subfolders = pattern == "*"
            ? matchedFolders
            : folder.EnumerateDirectories("*", SearchOption.TopDirectoryOnly);

        foreach (var subfolder in subfolders)
        {
            if (LinkHelper.IsLink(subfolder))
            {
                continue;
            }

            CollectEntries(subfolder, pattern, recursive, folderInfos, fileInfos);
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

    // The walk answers for the link itself: its length is the path it holds, and its target may be gone.
    // The walk reads the reparse-point attribute for free. Only a link pays for the probe that describes what
    // it points at.
    private async Task<FileSystemEntry> DescribeEntryAsync(
        FileSystemInfo entryInfo, StorageItemKind kind, long size)
    {
        var attributes = MapToPortable(entryInfo.Attributes);
        var modifiedUtc = entryInfo.LastWriteTimeUtc;

        bool isLink = LinkHelper.IsLink(entryInfo);
        if (isLink)
        {
            var targetInfoResult = await GetInfoAsync(entryInfo.FullName);
            if (targetInfoResult.IsSuccess)
            {
                var targetInfo = targetInfoResult.Value;
                kind = targetInfo.Kind;
                size = targetInfo.Size;
                modifiedUtc = targetInfo.ModifiedUtc;
                attributes = targetInfo.Attributes;
            }
        }

        var entry = new FileSystemEntry(
            FullPath: entryInfo.FullName,
            Kind: kind,
            Size: size,
            ModifiedUtc: modifiedUtc,
            Attributes: attributes,
            IsLink: isLink);

        return entry;
    }

    // Returns the final target of a link, or null when the link is broken. A resolve that throws counts as a
    // broken link. The final target is never itself a link, so describing it terminates.
    private static string? ResolveLinkTargetPath(FileInfo linkInfo)
    {
        try
        {
            var linkTarget = linkInfo.ResolveLinkTarget(returnFinalTarget: true);
            if (linkTarget is null
                || !linkTarget.Exists)
            {
                return null;
            }

            return linkTarget.FullName;
        }
        catch (IOException)
        {
            return null;
        }
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
