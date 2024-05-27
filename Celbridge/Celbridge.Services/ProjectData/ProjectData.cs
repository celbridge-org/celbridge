using LiteDB;
using Celbridge.BaseLibrary.Project;

namespace Celbridge.Services.ProjectData;

public class ProjectData : IDisposable, IProjectData
{
    private LiteDatabase _database;
    private bool _disposed = false;

    public ProjectConfig Config
    {
        get
        {
            var collection = _database.GetCollection<ProjectConfig>();
            var projectConfig = collection.FindAll().FirstOrDefault();
            return projectConfig ?? new ProjectConfig(0);
        }

        set
        {
            var collection = _database.GetCollection<ProjectConfig>();
            collection.DeleteAll();
            collection.Insert(value);
        }
    }

    private ProjectData(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path cannot be null or empty", nameof(databasePath));
        }

        _database = new LiteDatabase(databasePath);
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

    public static Result<IProjectData> CreateProjectData(string databasePath, ProjectConfig config)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return Result<IProjectData>.Fail($"Database path cannot be null or empty: {databasePath}");
        }

        var project = new ProjectData(databasePath);
        Guard.IsNotNull(project);

        project.Config = config;

        return Result<IProjectData>.Ok(project);
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
                _database?.Dispose();
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
