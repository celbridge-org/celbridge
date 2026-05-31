using Celbridge.FileSystem;
using Celbridge.FileSystem.Services;
using Celbridge.Logging;
using Celbridge.Resources;

namespace Celbridge.Packages;

/// <summary>
/// Reader used for bundled packages. Reads come straight off disk because
/// bundled assets sit outside any IResourceRegistry root and cannot be
/// addressed by a ResourceKey. Replaced by an assembly-resource reader when
/// the bundled-from-assembly migration lands.
/// </summary>
public sealed class DirectPackageReader : IPackageReader
{
    // Lazy default IFileSystem for the parameterless constructor — used by
    // PackageManifestLoader.LoadPackage and tests that aren't wired through DI.
    private static IFileSystem? _defaultFileSystem;
    private static readonly object _defaultFileSystemLock = new();

    private readonly IFileSystem _fileSystem;

    public DirectPackageReader()
        : this(GetDefaultFileSystem())
    {
    }

    public DirectPackageReader(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public bool Exists(string absolutePath)
    {
        var infoResult = SyncRunner.Run(() => _fileSystem.GetInfoAsync(absolutePath));
        return infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File;
    }

    public Result<string> ReadAllText(string absolutePath)
    {
        var readResult = SyncRunner.Run(() => _fileSystem.ReadAllTextAsync(absolutePath));
        if (readResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to read file: '{absolutePath}'")
                .WithErrors(readResult);
        }

        return readResult.Value;
    }

    public Result<byte[]> ReadAllBytes(string absolutePath)
    {
        var readResult = SyncRunner.Run(() => _fileSystem.ReadAllBytesAsync(absolutePath));
        if (readResult.IsFailure)
        {
            return Result<byte[]>.Fail($"Failed to read file: '{absolutePath}'")
                .WithErrors(readResult);
        }

        return readResult.Value;
    }

    private static IFileSystem GetDefaultFileSystem()
    {
        if (_defaultFileSystem is not null)
        {
            return _defaultFileSystem;
        }

        lock (_defaultFileSystemLock)
        {
            _defaultFileSystem ??= new LocalFileSystem(new SilentLogger<LocalFileSystem>());
            return _defaultFileSystem;
        }
    }

    // ILogger<T> stub used by the parameterless fallback path. The parameterless
    // reader runs in bootstrap-adjacent and test scenarios where the
    // project's NLog-backed logger is not wired up.
    private sealed class SilentLogger<T> : ILogger<T>
    {
        public void LogDebug(Exception? exception, string? message, params object?[] args) { }
        public void LogDebug(string? message, params object?[] args) { }
        public void LogTrace(Exception? exception, string? message, params object?[] args) { }
        public void LogTrace(string? message, params object?[] args) { }
        public void LogInformation(Exception? exception, string? message, params object?[] args) { }
        public void LogInformation(string? message, params object?[] args) { }
        public void LogWarning(Exception? exception, string? message, params object?[] args) { }
        public void LogWarning(string? message, params object?[] args) { }
        public void LogWarning(Result result, string? message, params object?[] args) { }
        public void LogError(Exception? exception, string? message, params object?[] args) { }
        public void LogError(string? message, params object?[] args) { }
        public void LogError(Result result, string? message, params object?[] args) { }
        public void LogCritical(Exception? exception, string? message, params object?[] args) { }
        public void LogCritical(string? message, params object?[] args) { }
        public void LogCritical(Result result, string? message, params object?[] args) { }
        public IDisposable? BeginScope(string messageFormat, params object?[] args) => null;
        public void Shutdown() { }
    }
}
