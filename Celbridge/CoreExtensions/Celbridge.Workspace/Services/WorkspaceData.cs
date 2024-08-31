using Celbridge.Workspace.Models;
using CommunityToolkit.Diagnostics;
using SQLite;
using System.Text.Json;

namespace Celbridge.Workspace.Services;

public class WorkspaceData : IDisposable, IWorkspaceData
{
    private const int DataVersion = 1;

    private SQLiteAsyncConnection _connection;

    public string DatabasePath { get; init; }

    private WorkspaceData(string databasePath)
    {
        Guard.IsNotNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;

        _connection = new SQLiteAsyncConnection(databasePath);
    }

    public async Task<Result<int>> GetDataVersionAsync()
    {
        var dataVersion = await _connection.Table<WorkspaceDataVersion>().FirstOrDefaultAsync();
        if (dataVersion == null)
        {
            return Result<int>.Fail($"Failed to get data version");
        }

        return Result<int>.Ok(dataVersion.Version);
    }

    public async Task<Result> SetDataVersionAsync(int version)
    {
        var dataVersion = await _connection.Table<WorkspaceDataVersion>().FirstOrDefaultAsync();
        if (dataVersion == null)
        {
            return Result.Fail($"Failed to get data version");
        }

        dataVersion.Version = version;

        await _connection.UpdateAsync(dataVersion);

        return Result.Ok();
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

    public static Result<IWorkspaceData> LoadWorkspaceData(string databasePath)
    {
        Guard.IsNotNullOrWhiteSpace(databasePath);

        try
        {
            var workspaceData = new WorkspaceData(databasePath);
            Guard.IsNotNull(workspaceData);

            return Result<IWorkspaceData>.Ok(workspaceData);
        }
        catch (Exception ex)
        {
            return Result<IWorkspaceData>.Fail($"Failed to load workspace database. {ex.Message}");
        }
    }

    public static async Task<Result> CreateWorkspaceDataAsync(string databasePath)
    {
        Guard.IsNotNullOrWhiteSpace(databasePath);

        var workspaceData = new WorkspaceData(databasePath);
        Guard.IsNotNull(workspaceData);

        // Todo: Store version number as a property instead of a dedicated table
        await workspaceData._connection.CreateTableAsync<WorkspaceDataVersion>();
        var dataVersion = new WorkspaceDataVersion 
        { 
            Version = DataVersion
        };
        await workspaceData._connection.InsertAsync(dataVersion);

        await workspaceData._connection.CreateTableAsync<WorkspaceProperty>();

        // Close the database
        workspaceData.Dispose();

        return Result.Ok();
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

    ~WorkspaceData()
    {
        Dispose(false);
    }
}
