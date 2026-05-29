using Celbridge.Resources;

namespace Celbridge.Packages;

/// <summary>
/// Reader used for project packages. Reverse-resolves the absolute path to a
/// ResourceKey via IResourceRegistry and routes every read through IFileStorage
/// so the chokepoint contract holds for project-tree bytes.
///
/// IPackageReader is sync to match its sync callers (PackageManifestLoader,
/// PackageLocalizationService) but IFileStorage is genuinely async. Each call
/// is dispatched through Task.Run before the blocking wait so the async work
/// starts on a thread-pool thread whose continuations do not try to resume on
/// the caller's SynchronizationContext. Without that, calling this reader from
/// the UI thread (workspace load, template fetch) deadlocks: the await
/// continuations inside FileStorage would post back to the UI thread the
/// outer GetResult is blocking.
/// </summary>
public sealed class FileStoragePackageReader : IPackageReader
{
    private readonly IFileStorage _fileStorage;
    private readonly IResourceRegistry _resourceRegistry;

    public FileStoragePackageReader(
        IFileStorage fileStorage,
        IResourceRegistry resourceRegistry)
    {
        _fileStorage = fileStorage;
        _resourceRegistry = resourceRegistry;
    }

    public bool Exists(string absolutePath)
    {
        var keyResult = _resourceRegistry.GetResourceKey(absolutePath);
        if (keyResult.IsFailure)
        {
            return false;
        }

        var infoResult = Task.Run(() => _fileStorage.GetInfoAsync(keyResult.Value))
            .GetAwaiter()
            .GetResult();
        if (infoResult.IsFailure)
        {
            return false;
        }

        return infoResult.Value.Kind != StorageItemKind.NotFound;
    }

    public Result<string> ReadAllText(string absolutePath)
    {
        var keyResult = _resourceRegistry.GetResourceKey(absolutePath);
        if (keyResult.IsFailure)
        {
            return Result<string>.Fail($"Could not resolve resource key for path: '{absolutePath}'")
                .WithErrors(keyResult);
        }

        return Task.Run(() => _fileStorage.ReadAllTextAsync(keyResult.Value))
            .GetAwaiter()
            .GetResult();
    }

    public Result<byte[]> ReadAllBytes(string absolutePath)
    {
        var keyResult = _resourceRegistry.GetResourceKey(absolutePath);
        if (keyResult.IsFailure)
        {
            return Result<byte[]>.Fail($"Could not resolve resource key for path: '{absolutePath}'")
                .WithErrors(keyResult);
        }

        return Task.Run(() => _fileStorage.ReadAllBytesAsync(keyResult.Value))
            .GetAwaiter()
            .GetResult();
    }
}
