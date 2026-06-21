using System.Text.Json;
using Celbridge.Logging;
using Celbridge.Settings;
using Celbridge.Utilities;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// The per-project JSON store. Backs both the eager IWorkspacePropertyBag and the
/// deferred ISettingsStore.
/// </summary>
public sealed class WorkspaceStore : IWorkspacePropertyBag, ISettingsStore
{
    private const int DataVersion = 1;
    private const string DataVersionKey = nameof(DataVersion);

    private readonly ILocalFileSystem _fileSystem;
    private readonly string _filePath;
    private readonly KeyValueStore _store;

    private bool _isDirty;

    private WorkspaceStore(ILocalFileSystem fileSystem, string filePath, KeyValueStore store)
    {
        _fileSystem = fileSystem;
        _filePath = filePath;
        _store = store;
    }

    public async Task<int> GetDataVersionAsync()
    {
        return await GetPropertyAsync(DataVersionKey, 0);
    }

    public async Task SetDataVersionAsync(int version)
    {
        await SetPropertyAsync(DataVersionKey, version);
    }

    public async Task SetPropertyAsync<T>(string key, T value) where T : notnull
    {
        try
        {
            _store.SetValue(key, value);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to set workspace property for key {key}", ex);
        }

        await PersistAsync();
    }

    public async Task<T?> GetPropertyAsync<T>(string key, T? defaultValue)
    {
        await Task.CompletedTask;

        try
        {
            if (!_store.TryGetSerialized(key, out var json))
            {
                return defaultValue;
            }

            var value = JsonSerializer.Deserialize<T>(json);
            if (value is null)
            {
                return defaultValue;
            }

            return value;
        }
        catch (JsonException ex)
        {
            // A stored value that cannot be deserialized (corruption, or a schema change
            // across versions) is treated as absent rather than failing the read. The
            // workspace state is regenerable and is rewritten on the next store, so a
            // single malformed property must not abort the whole workspace load.
            var logger = ServiceLocator.AcquireService<ILogger<WorkspaceStore>>();
            logger.LogWarning(ex, "Discarding malformed workspace property '{Key}'; using the default value", key);
            return defaultValue;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to get workspace property for key {key}", ex);
        }
    }

    public async Task<T?> GetPropertyAsync<T>(string key)
    {
        var defaultValue = default(T);
        return await GetPropertyAsync<T>(key, defaultValue);
    }

    public async Task<bool> DeletePropertyAsync(string key)
    {
        if (!_store.Remove(key))
        {
            return false;
        }

        await PersistAsync();

        return true;
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

        // PersistAsync throws on failure (the async property-bag writes rely on
        // that); convert it to a Result for the ISettingsStore flush contract.
        try
        {
            await PersistAsync();
        }
        catch (Exception exception)
        {
            return Result.Fail("Failed to flush workspace settings")
                .WithException(exception);
        }

        return Result.Ok();
    }

    // Overwrites the settings file with the full in-memory store. The data is
    // regenerable UI state, so a write interrupted by a crash is acceptable: the
    // next load discards an unreadable file and starts empty.
    private async Task PersistAsync()
    {
        string json;
        try
        {
            json = _store.ToJson();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to serialize workspace settings", ex);
        }

        var writeResult = await _fileSystem.WriteAllTextAsync(_filePath, json);
        if (writeResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to persist workspace settings: {writeResult.FirstErrorMessage}");
        }

        _isDirty = false;
    }

    /// <summary>
    /// Loads the workspace store from the JSON file at the given path, returning an
    /// empty store when the file is missing or unreadable. Ensures the data version
    /// is recorded, creating the file on first load.
    /// </summary>
    public static async Task<Result<WorkspaceStore>> LoadAsync(ILocalFileSystem fileSystem, string filePath)
    {
        Guard.IsNotNullOrWhiteSpace(filePath);

        KeyValueStore store;

        var infoResult = await fileSystem.GetInfoAsync(filePath);
        bool fileExists = infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File;

        if (fileExists)
        {
            var readResult = await fileSystem.ReadAllTextAsync(filePath);
            if (readResult.IsFailure)
            {
                return Result<WorkspaceStore>.Fail($"Failed to read workspace settings file: {filePath}")
                    .WithErrors(readResult);
            }

            // A corrupt store is treated as empty rather than failing the workspace
            // load; the data is regenerable UI state.
            store = KeyValueStore.FromJson(readResult.Value);
        }
        else
        {
            store = new KeyValueStore();
        }

        var workspaceStore = new WorkspaceStore(fileSystem, filePath, store);

        if (!workspaceStore.ContainsKey(DataVersionKey))
        {
            await workspaceStore.SetDataVersionAsync(DataVersion);
        }

        return workspaceStore;
    }
}
