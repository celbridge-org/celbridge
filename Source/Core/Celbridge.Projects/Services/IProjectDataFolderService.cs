namespace Celbridge.Projects.Services;

/// <summary>
/// Manages the data folders in the application data folder that contain each project's local data. Each project
/// folder has its own data folder, found from the project folder's path.
/// </summary>
internal interface IProjectDataFolderService
{
    /// <summary>
    /// Returns the path of the data folder for the folder of the project file at the given path. The data folder
    /// may not exist yet.
    /// </summary>
    string GetDataFolderPath(string projectFilePath);

    /// <summary>
    /// Creates the data folder for the folder of the project file at the given path if needed, with a
    /// project.json that names the project folder. Returns the data folder's path. Fails if no project file
    /// exists at the path.
    /// </summary>
    Task<Result<string>> EnsureDataFolderAsync(string projectFilePath);

    /// <summary>
    /// Deletes the data folder for the folder of the project file at the given path, if the data folder exists.
    /// </summary>
    Task<Result> ClearDataFolderAsync(string projectFilePath);
}
