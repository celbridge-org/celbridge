namespace Celbridge.Projects.Services;

/// <summary>
/// Owns the data folders that hold each project's local data, in the application data folder. A project
/// file's data folder is found from its path, so each project file has one of its own.
/// </summary>
internal interface IDataFolderService
{
    /// <summary>
    /// The path of the data folder that belongs to the project file at the given path. The folder may not
    /// exist yet.
    /// </summary>
    string GetDataFolderPath(string projectFilePath);

    /// <summary>
    /// Creates the data folder of the project file at the given path, with the project.json that names the
    /// project file, unless the folder exists. Returns the folder's path.
    /// </summary>
    Task<Result<string>> EnsureDataFolderAsync(string projectFilePath);

    /// <summary>
    /// Deletes the data folder of the project file at the given path, so a project created there starts
    /// without the data of one that was there before.
    /// </summary>
    Task<Result> ClearDataFolderAsync(string projectFilePath);
}
