using Celbridge.Workspace.Models;
using CommunityToolkit.Diagnostics;
using SQLite;
using System.Text.Json;

namespace Celbridge.Workspace.Services;

public class WorkspaceSettings : IDisposable, IWorkspaceSettings
{
    private const int DataVersion = 1;
    private const string DataVersionKey = nameof(DataVersion);

    private SQLiteAsyncConnection _connection;

    public string DatabasePath { get; init; }

    private WorkspaceSettings(string databasePath)
    {
        Guard.IsNotNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;

        _connection = new SQLiteAsyncConnection(databasePath);
    }

    public async Task SetDataVersionAsync(int version)
    {
        await SetPropertyAsync(DataVersionKey, version);
    }

    public async Task<int> GetDataVersionAsync()
    {
        return await GetPropertyAsync(DataVersionKey, 0);
    }

    public async Task SetPropertyAsync<T>(string key, T value) where T : notnull
    {
        try
        {
            var serializedValue = JsonSerializer.Serialize(value);
 
            var property = new WorkspaceProperty()
            {
                Key = key,
                Value = serializedValue
            };

            var addedRows = await _connection.InsertOrReplaceAsync(property);
            Guard.IsTrue(addedRows <= 1);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to set workspace property for key {key}", ex);
        }
    }

    public async Task<T?> GetPropertyAsync<T>(string key, T? defaultValue)
    {
        try
        {
            var property = await _connection.Table<WorkspaceProperty>().FirstOrDefaultAsync(p => p.Key == key);

            if (property == null)
            {
                return defaultValue;
            }

            var value = JsonSerializer.Deserialize<T>(property.Value);
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

    public static Result<IWorkspaceSettings> LoadWorkspaceSettings(string databasePath)
    {
        Guard.IsNotNullOrWhiteSpace(databasePath);

        try
        {
            var workspaceSettings = new WorkspaceSettings(databasePath);
            Guard.IsNotNull(workspaceSettings);

            return Result<IWorkspaceSettings>.Ok(workspaceSettings);
        }
        catch (Exception ex)
        {
            return Result<IWorkspaceSettings>.Fail($"Failed to load workspace settings database. {ex.Message}");
        }
    }

    public static async Task<Result> CreateWorkspaceSettingsAsync(string databasePath)
    {
        Guard.IsNotNullOrWhiteSpace(databasePath);

        try
        {
            // Ensure parent folder exists
            var parentFolder = Path.GetDirectoryName(databasePath);
            Guard.IsNotNull(parentFolder);

            if (!Directory.Exists(parentFolder))
            {
                Directory.CreateDirectory(parentFolder);

#if WINDOWS
                // Hide the folder in windows explorer
                var attributes = File.GetAttributes(parentFolder);
                File.SetAttributes(parentFolder, attributes | System.IO.FileAttributes.Hidden);
#endif
            }

            // Create and initialize the workspace settings database
            using (var workspaceSettings = new WorkspaceSettings(databasePath))
            {
                Guard.IsNotNull(workspaceSettings);

                await workspaceSettings._connection.CreateTableAsync<WorkspaceProperty>();
                await workspaceSettings.SetDataVersionAsync(DataVersion);
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, "An exception occurred when creating the workspace settings database");
        }
    }

    private bool _disposed = false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _connection?.CloseAsync().Wait();
            }

            _disposed = true;
        }
    }

    ~WorkspaceSettings()
    {
        Dispose(false);
    }
}
