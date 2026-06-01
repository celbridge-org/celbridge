using System.Collections.Concurrent;
using System.Text;
using Celbridge.Resources;

namespace Celbridge.Tests.FileSystem;

/// <summary>
/// In-memory <see cref="ILocalFileSystem"/> for tests. Records every call so tests
/// can assert call sequences without bespoke spy infrastructure. Paths are
/// case-sensitive — the gateway contract is path-string-in / path-string-out,
/// so the fake matches what the caller passed exactly.
/// </summary>
public sealed class FakeFileSystem : ILocalFileSystem
{
    public sealed record FileEntry(byte[] Bytes, DateTime ModifiedUtc, FileSystemAttributes Attributes);

    private readonly ConcurrentDictionary<string, FileEntry> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _folders = new(StringComparer.Ordinal);
    private readonly List<string> _calls = new();
    private readonly object _foldersLock = new();
    private readonly object _callsLock = new();

    public IReadOnlyDictionary<string, FileEntry> Files => _files;
    public IReadOnlyCollection<string> Folders
    {
        get
        {
            lock (_foldersLock)
            {
                return _folders.ToArray();
            }
        }
    }

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_callsLock)
            {
                return _calls.ToArray();
            }
        }
    }

    public void Reset()
    {
        _files.Clear();
        lock (_foldersLock)
        {
            _folders.Clear();
        }
        lock (_callsLock)
        {
            _calls.Clear();
        }
    }

    public void SeedFile(string path, byte[] bytes, DateTime? modifiedUtc = null, FileSystemAttributes attributes = FileSystemAttributes.None)
    {
        _files[path] = new FileEntry(bytes, modifiedUtc ?? DateTime.UtcNow, attributes);
        AddFolderAndAncestors(GetParent(path));
    }

    public void SeedFile(string path, string content, DateTime? modifiedUtc = null, FileSystemAttributes attributes = FileSystemAttributes.None)
    {
        SeedFile(path, Encoding.UTF8.GetBytes(content), modifiedUtc, attributes);
    }

    public void SeedFolder(string path)
    {
        AddFolderAndAncestors(path);
    }

    public Task<Result<byte[]>> ReadAllBytesAsync(string path)
    {
        Record($"ReadAllBytesAsync('{path}')");
        if (!_files.TryGetValue(path, out var entry))
        {
            return Task.FromResult(Result<byte[]>.Fail($"File not found: '{path}'"));
        }

        return Task.FromResult<Result<byte[]>>(entry.Bytes);
    }

    public Task<Result<string>> ReadAllTextAsync(string path)
    {
        Record($"ReadAllTextAsync('{path}')");
        if (!_files.TryGetValue(path, out var entry))
        {
            return Task.FromResult(Result<string>.Fail($"File not found: '{path}'"));
        }

        return Task.FromResult<Result<string>>(Encoding.UTF8.GetString(entry.Bytes));
    }

    public Task<Result<Stream>> OpenReadAsync(string path)
    {
        Record($"OpenReadAsync('{path}')");
        if (!_files.TryGetValue(path, out var entry))
        {
            return Task.FromResult(Result<Stream>.Fail($"File not found: '{path}'"));
        }

        Stream stream = new MemoryStream(entry.Bytes, writable: false);
        return Task.FromResult<Result<Stream>>(stream);
    }

    public Task<Result<Stream>> OpenWriteAsync(string path, WriteMode mode)
    {
        Record($"OpenWriteAsync('{path}', mode={mode})");

        if (mode == WriteMode.CreateNew
            && _files.ContainsKey(path))
        {
            return Task.FromResult<Result<Stream>>(Result.Fail($"File already exists: '{path}'"));
        }

        var stream = new CommittingMemoryStream(this, path);
        if (mode == WriteMode.Append
            && _files.TryGetValue(path, out var existing))
        {
            stream.Write(existing.Bytes);
            // Position lands at the end after the seed write, so subsequent
            // caller writes append exactly as the real backend would.
        }

        return Task.FromResult<Result<Stream>>((Stream)stream);
    }

    public Task<Result> WriteAllBytesAsync(string path, byte[] bytes)
    {
        Record($"WriteAllBytesAsync('{path}', {bytes.Length} bytes)");
        SeedFile(path, bytes);
        return Task.FromResult(Result.Ok());
    }

    public Task<Result> WriteAllTextAsync(string path, string content)
    {
        Record($"WriteAllTextAsync('{path}', {content.Length} chars)");
        SeedFile(path, content);
        return Task.FromResult(Result.Ok());
    }

    public Task<Result<StorageItemInfo>> GetInfoAsync(string path)
    {
        Record($"GetInfoAsync('{path}')");
        if (_files.TryGetValue(path, out var entry))
        {
            var fileInfo = new StorageItemInfo(StorageItemKind.File, entry.Bytes.Length, entry.ModifiedUtc, entry.Attributes);
            return Task.FromResult<Result<StorageItemInfo>>(fileInfo);
        }

        lock (_foldersLock)
        {
            if (_folders.Contains(path))
            {
                var folderInfo = new StorageItemInfo(StorageItemKind.Folder, 0, DateTime.UtcNow, FileSystemAttributes.None);
                return Task.FromResult<Result<StorageItemInfo>>(folderInfo);
            }
        }

        var notFound = new StorageItemInfo(StorageItemKind.NotFound, 0, default, FileSystemAttributes.None);
        return Task.FromResult<Result<StorageItemInfo>>(notFound);
    }

    public Task<Result<IReadOnlyList<string>>> EnumerateFilesAsync(string path, string pattern, bool recursive, Func<string, bool>? validateEntry = null)
    {
        Record($"EnumerateFilesAsync('{path}', '{pattern}', recursive={recursive})");
        var normalizedPath = NormalizeFolder(path);
        var matches = new List<string>();
        foreach (var filePath in _files.Keys)
        {
            if (!IsUnder(filePath, normalizedPath, recursive))
            {
                continue;
            }
            var fileName = GetName(filePath);
            if (!MatchesPattern(fileName, pattern))
            {
                continue;
            }
            if (validateEntry is not null
                && !validateEntry(filePath))
            {
                continue;
            }
            matches.Add(filePath);
        }

        IReadOnlyList<string> list = matches;
        return Task.FromResult(Result<IReadOnlyList<string>>.Ok(list));
    }

    public Task<Result<IReadOnlyList<string>>> EnumerateFoldersAsync(string path)
    {
        Record($"EnumerateFoldersAsync('{path}')");
        var normalizedPath = NormalizeFolder(path);
        var matches = new List<string>();
        lock (_foldersLock)
        {
            foreach (var folder in _folders)
            {
                if (folder.Length <= normalizedPath.Length)
                {
                    continue;
                }
                if (!folder.StartsWith(normalizedPath, StringComparison.Ordinal))
                {
                    continue;
                }
                var suffix = folder.Substring(normalizedPath.Length);
                if (suffix.Contains('/')
                    || suffix.Contains('\\'))
                {
                    continue;
                }
                matches.Add(folder);
            }
        }

        IReadOnlyList<string> list = matches;
        return Task.FromResult(Result<IReadOnlyList<string>>.Ok(list));
    }

    public Task<Result> MoveFileAsync(string source, string dest)
    {
        Record($"MoveFileAsync('{source}', '{dest}')");
        if (!_files.TryRemove(source, out var entry))
        {
            return Task.FromResult<Result>(Result.Fail($"File not found: '{source}'"));
        }
        SeedFile(dest, entry.Bytes, entry.ModifiedUtc, entry.Attributes);
        return Task.FromResult(Result.Ok());
    }

    public Task<Result> MoveFolderAsync(string source, string dest)
    {
        Record($"MoveFolderAsync('{source}', '{dest}')");
        var normalizedSource = NormalizeFolder(source);
        var normalizedDest = NormalizeFolder(dest);

        var movedFiles = new List<(string oldPath, string newPath, FileEntry entry)>();
        foreach (var key in _files.Keys.ToArray())
        {
            if (!IsUnder(key, normalizedSource, recursive: true))
            {
                continue;
            }
            var relative = key.Substring(normalizedSource.Length);
            var newPath = normalizedDest + relative;
            if (_files.TryRemove(key, out var entry))
            {
                movedFiles.Add((key, newPath, entry));
            }
        }
        foreach (var moved in movedFiles)
        {
            SeedFile(moved.newPath, moved.entry.Bytes, moved.entry.ModifiedUtc, moved.entry.Attributes);
        }

        lock (_foldersLock)
        {
            _folders.Remove(source);
            _folders.Add(dest);
        }

        return Task.FromResult(Result.Ok());
    }

    public Task<Result> CopyFileAsync(string source, string dest)
    {
        Record($"CopyFileAsync('{source}', '{dest}')");
        if (!_files.TryGetValue(source, out var entry))
        {
            return Task.FromResult<Result>(Result.Fail($"File not found: '{source}'"));
        }
        SeedFile(dest, entry.Bytes, entry.ModifiedUtc, entry.Attributes);
        return Task.FromResult(Result.Ok());
    }

    public Task<Result> DeleteFileAsync(string path)
    {
        Record($"DeleteFileAsync('{path}')");
        if (!_files.TryRemove(path, out _))
        {
            return Task.FromResult<Result>(Result.Fail($"File not found: '{path}'"));
        }
        return Task.FromResult(Result.Ok());
    }

    public Task<Result> DeleteFolderAsync(string path, bool recursive)
    {
        Record($"DeleteFolderAsync('{path}', recursive={recursive})");
        var normalized = NormalizeFolder(path);

        foreach (var key in _files.Keys.ToArray())
        {
            if (IsUnder(key, normalized, recursive: true))
            {
                _files.TryRemove(key, out _);
            }
        }

        lock (_foldersLock)
        {
            _folders.RemoveWhere(folder =>
                folder == path
                || folder.StartsWith(normalized, StringComparison.Ordinal));
        }

        return Task.FromResult(Result.Ok());
    }

    public Task<Result> CreateFolderAsync(string path)
    {
        Record($"CreateFolderAsync('{path}')");
        AddFolderAndAncestors(path);
        return Task.FromResult(Result.Ok());
    }

    public Task<Result> SetAttributesAsync(string path, FileSystemAttributes mask, bool set)
    {
        Record($"SetAttributesAsync('{path}', mask={mask}, set={set})");
        if (!_files.TryGetValue(path, out var entry))
        {
            return Task.FromResult<Result>(Result.Fail($"File not found: '{path}'"));
        }

        // Apply only the masked bits; preserve everything else.
        FileSystemAttributes updated;
        if (set)
        {
            updated = entry.Attributes | mask;
        }
        else
        {
            updated = entry.Attributes & ~mask;
        }
        _files[path] = entry with { Attributes = updated };
        return Task.FromResult(Result.Ok());
    }

    private void Record(string call)
    {
        lock (_callsLock)
        {
            _calls.Add(call);
        }
    }

    private void AddFolderAndAncestors(string? folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        lock (_foldersLock)
        {
            _folders.Add(folder);
            var parent = GetParent(folder);
            while (!string.IsNullOrEmpty(parent))
            {
                if (!_folders.Add(parent))
                {
                    break;
                }
                parent = GetParent(parent);
            }
        }
    }

    private static string? GetParent(string path)
    {
        var lastSlash = path.LastIndexOfAny(new[] { '/', '\\' });
        if (lastSlash <= 0)
        {
            return null;
        }
        return path.Substring(0, lastSlash);
    }

    private static string GetName(string path)
    {
        var lastSlash = path.LastIndexOfAny(new[] { '/', '\\' });
        return lastSlash < 0 ? path : path.Substring(lastSlash + 1);
    }

    private static string NormalizeFolder(string folder)
    {
        if (folder.EndsWith('/')
            || folder.EndsWith('\\'))
        {
            return folder;
        }
        return folder + "/";
    }

    private static bool IsUnder(string filePath, string folderWithSeparator, bool recursive)
    {
        if (!filePath.StartsWith(folderWithSeparator, StringComparison.Ordinal))
        {
            return false;
        }
        if (recursive)
        {
            return true;
        }
        var suffix = filePath.Substring(folderWithSeparator.Length);
        return !suffix.Contains('/')
            && !suffix.Contains('\\');
    }

    // MemoryStream that flushes its buffer back into the parent file dictionary
    // when the caller disposes the stream. Mirrors LocalFileSystem.OpenWriteAsync
    // truncate-or-create semantics: the file is created (or replaced) on Dispose.
    private sealed class CommittingMemoryStream : MemoryStream
    {
        private readonly FakeFileSystem _owner;
        private readonly string _path;
        private bool _committed;

        public CommittingMemoryStream(FakeFileSystem owner, string path)
        {
            _owner = owner;
            _path = path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing
                && !_committed)
            {
                _committed = true;
                var bytes = ToArray();
                _owner.SeedFile(_path, bytes);
            }
            base.Dispose(disposing);
        }
    }

    // Supports the common cases of "*", "*.ext", and "name.ext" patterns; the
    // gateway's enumerate is most often called with one of these.
    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (pattern == "*"
            || pattern == "*.*")
        {
            return true;
        }
        if (pattern.StartsWith("*.")
            && pattern.IndexOf('*', 1) == -1)
        {
            var extension = pattern.Substring(1);
            return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(fileName, pattern, StringComparison.Ordinal);
    }
}
