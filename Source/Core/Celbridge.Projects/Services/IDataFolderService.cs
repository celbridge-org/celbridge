namespace Celbridge.Projects.Services;

/// <summary>
/// Manages the data folders in the application data folder that contain each project's local data. Each project
/// file has its own data folder, found from the project file's path.
/// </summary>
internal interface IDataFolderService
{
    /// <summary>
    /// Returns the path of the data folder for the project file at the given path. The folder may not exist
    /// yet.
    /// </summary>
    string GetDataFolderPath(string projectFilePath);

    /// <summary>
    /// Creates the data folder for the project file at the given path if needed, with a project.json that
    /// names the project file. Returns the folder's path. Fails if no project file exists at the path.
    /// </summary>
    Task<Result<string>> EnsureDataFolderAsync(string projectFilePath);

    /// <summary>
    /// Deletes the data folder for the project file at the given path, if it exists.
    /// </summary>
    Task<Result> ClearDataFolderAsync(string projectFilePath);
}
