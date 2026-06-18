using System.Text.Json;
using Celbridge.Workspace;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// Per-project settings stored as a JSON key/value file in the project folder,
/// loaded into memory once. The async IWorkspaceSettings facade persists on each
/// write; the synchronous IWorkspaceSettingsStore updates memory only and defers
/// the disk write to FlushAsync.
/// </summary>
public sealed class WorkspaceSettings : IWorkspaceSettings, IWorkspaceSettingsStore
{
    private const int DataVersion = 1;
    private const string DataVersionKey = nameof(DataVersion);

    private static readonly JsonSerializerOptions FileSerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly ILocalFileSystem _fileSystem;
    private readonly string _filePath;
    private readonly Dictionary<string, string> _entries;

    private bool _isDirty;

    private WorkspaceSettings(ILocalFileSystem fileSystem, string filePath, Dictionary<string, string> entries)
    {
        _fileSystem = fileSystem;
        _filePath = filePath;
        _entries = entries;
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
            _entries[key] = JsonSerializer.Serialize(value);
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
            if (!_entries.TryGetValue(key, out var json))
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
        if (!_entries.Remove(key))
        {
            return false;
        }

        await PersistAsync();

        return true;
    }

    public bool TryGetValue<T>(string key, out T value) where T : notnull
    {
        value = default!;

        if (!_entries.TryGetValue(key, out var json))
        {
            return false;
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize<T>(json);
            if (deserialized is null)
            {
                return false;
            }

            value = deserialized;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void SetValue<T>(string key, T value) where T : notnull
    {
        _entries[key] = JsonSerializer.Serialize(value);
        _isDirty = true;
    }

    public bool ContainsKey(string key)
    {
        return _entries.ContainsKey(key);
    }

    public void RemoveValue(string key)
    {
        if (_entries.Remove(key))
        {
            _isDirty = true;
        }
    }

    public async Task FlushAsync()
    {
        if (!_isDirty)
        {
            return;
        }

        await PersistAsync();
    }

    // Overwrites the settings file with the full in-memory store. The data is
    // regenerable UI state, so a write interrupted by a crash is acceptable: the
    // next load discards an unreadable file and starts empty.
    private async Task PersistAsync()
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(_entries, FileSerializerOptions);
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
    /// Loads the workspace settings from the JSON file at the given path,
    /// returning an empty store when the file is missing or unreadable. Ensures
    /// the data version is recorded, creating the file on first load.
    /// </summary>
    public static async Task<Result<WorkspaceSettings>> LoadAsync(ILocalFileSystem fileSystem, string filePath)
    {
        Guard.IsNotNullOrWhiteSpace(filePath);

        var entries = new Dictionary<string, string>();

        var infoResult = await fileSystem.GetInfoAsync(filePath);
        bool fileExists = infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File;

        if (fileExists)
        {
            var readResult = await fileSystem.ReadAllTextAsync(filePath);
            if (readResult.IsFailure)
            {
                return Result<WorkspaceSettings>.Fail($"Failed to read workspace settings file: {filePath}")
                    .WithErrors(readResult);
            }

            var content = readResult.Value;
            if (!string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
                    if (parsed is not null)
                    {
                        entries = parsed;
                    }
                }
                catch (JsonException)
                {
                    // A corrupt store is treated as empty rather than failing the
                    // workspace load; the data is regenerable UI state.
                }
            }
        }

        var workspaceSettings = new WorkspaceSettings(fileSystem, filePath, entries);

        if (!workspaceSettings.ContainsKey(DataVersionKey))
        {
            await workspaceSettings.SetDataVersionAsync(DataVersion);
        }

        return workspaceSettings;
    }
}
