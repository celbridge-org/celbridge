using Celbridge.ApplicationEnvironment;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Celbridge.Utilities;

namespace Celbridge.Settings.Services;

/// <summary>
/// The Application-scope ISettingsStore, backed by a JSON file in the per-user
/// config folder.
/// </summary>
internal sealed class ApplicationStore : ISettingsStore
{
    private const string SettingsFileName = "settings.json";

    private readonly ILogger<ApplicationStore> _logger;
    private readonly ILocalFileSystem _fileSystem;
    private readonly string _filePath;
    private readonly KeyValueStore _store;

    private bool _isDirty;

    public ApplicationStore(ILogger<ApplicationStore> logger, ILocalFileSystem fileSystem, IAppEnvironment appEnvironment)
        : this(logger, fileSystem, ResolveDefaultFilePath(appEnvironment.LocalApplicationDataFolderPath))
    {
    }

    // Lets tests point the store at a temporary file. The public constructor above
    // is the one DI resolves.
    internal ApplicationStore(ILogger<ApplicationStore> logger, ILocalFileSystem fileSystem, string filePath)
    {
        _logger = logger;
        _fileSystem = fileSystem;
        _filePath = filePath;
        _store = Load();
    }

    public bool TryGetValue<T>(string key, out T value) where T : notnull
    {
        return _store.TryGetValue(key, out value);
    }

    public void SetValue<T>(string key, T value) where T : notnull
    {
        _store.SetValue(key, value);
        _isDirty = true;
    }

    public bool ContainsKey(string key)
    {
        return _store.ContainsKey(key);
    }

    public void RemoveValue(string key)
    {
        if (_store.Remove(key))
        {
            _isDirty = true;
        }
    }

    public async Task<Result> FlushAsync()
    {
        if (!_isDirty)
        {
            return Result.Ok();
        }

        // The settings folder may not exist on a first run (the per-user Celbridge
        // folder on macOS and Linux). WriteAllTextAsync does not create it, so ensure
        // it exists first.
        var settingsFolder = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(settingsFolder))
        {
            var createFolderResult = await _fileSystem.CreateFolderAsync(settingsFolder);
            if (createFolderResult.IsFailure)
            {
                // Leave the store dirty so the next flush retries.
                return Result.Fail($"Failed to create application settings folder: {settingsFolder}")
                    .WithErrors(createFolderResult);
            }
        }

        var writeResult = await _fileSystem.WriteAllTextAsync(_filePath, _store.ToJson());
        if (writeResult.IsFailure)
        {
            // Leave the store dirty so the next flush retries.
            return Result.Fail($"Failed to persist application settings: {_filePath}")
                .WithErrors(writeResult);
        }

        _isDirty = false;

        return Result.Ok();
    }

    // Composes the settings file path within the per-user Celbridge application-data folder. The folder
    // itself (already private to Celbridge on every head) is resolved by IAppEnvironment.
    public static string ResolveDefaultFilePath(string localApplicationDataFolder)
    {
        return Path.Combine(localApplicationDataFolder, SettingsFileName);
    }

    // Reads the store once at construction. Settings are read synchronously
    // throughout, so the file must be loaded before the first read; SyncRunner
    // offloads to the thread pool, so blocking here cannot deadlock on a captured
    // UI context. A missing or unreadable file loads as an empty store.
    private KeyValueStore Load()
    {
        var infoResult = SyncRunner.Run(() => _fileSystem.GetInfoAsync(_filePath));
        bool fileExists = infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File;

        if (!fileExists)
        {
            return new KeyValueStore();
        }

        var readResult = SyncRunner.Run(() => _fileSystem.ReadAllTextAsync(_filePath));
        if (readResult.IsFailure)
        {
            _logger.LogWarning($"Failed to read application settings file '{_filePath}'; starting with empty settings");

            return new KeyValueStore();
        }

        return KeyValueStore.FromJson(readResult.Value);
    }
}
