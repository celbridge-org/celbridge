namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Manages the user data associated with a Celbridge project.
/// </summary>
public interface IProjectUserData
{
    /// <summary>
    /// Gets the data version for the project user data.
    /// </summary>
    Task<Result<int>> GetDataVersionAsync();

    /// <summary>
    /// Sets the data version for the project user data.
    /// </summary>
    Task<Result> SetDataVersionAsync(int version);

    /// <summary>
    /// 
    /// </summary>
    Task<Result<List<string>>> GetExpandedFoldersAsync();

    /// <summary>
    /// 
    /// </summary>
    Task<Result> SetExpandedFoldersAsync(List<string> folderNames);
}
