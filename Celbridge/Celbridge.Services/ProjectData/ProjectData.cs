using SQLite;
using Celbridge.BaseLibrary.Project;

namespace Celbridge.Services.ProjectData;

public class ProjectData : IDisposable, IProjectData
{
    private SQLiteConnection _connection;
    private bool _disposed = false;

    private ProjectData(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path cannot be null or empty", nameof(databasePath));
        }

        _connection = new SQLiteConnection(databasePath);
    }

    public IProjectConfig Config
    {
        get
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(_connection));
            }

            var config = _connection.Table<ProjectConfig>().FirstOrDefault();
            if (config == null)
            {
                throw new InvalidOperationException("ProjectConfig table not found in database.");
            }

            return config;
        }

        set
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(_connection));
            }

            if (value == null)
            {
                throw new ArgumentNullException();
            }

            _connection.Update(value);
        }
    }

    public static Result<IProjectData> LoadProjectData(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return Result<IProjectData>.Fail($"Database path cannot be null or empty: {databasePath}");
        }

        var project = new ProjectData(databasePath);
        Guard.IsNotNull(project);

        return Result<IProjectData>.Ok(project);
    }

    public static Result CreateProjectData(string databasePath, int version)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return Result<IProjectData>.Fail($"Database path cannot be null or empty: {databasePath}");
        }

        var project = new ProjectData(databasePath);
        Guard.IsNotNull(project);

        var config = new ProjectConfig 
        { 
            Version = version 
        };

        project._connection.CreateTable<ProjectConfig>();
        project._connection.Insert(config);

        // Close the database after creating it
        project.Dispose();

        return Result.Ok();
    }

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
                // Dispose managed resources
                _connection?.Close();
            }

            // Dispose unmanaged resources here if any

            _disposed = true;
        }
    }

    ~ProjectData()
    {
        Dispose(false);
    }
}
