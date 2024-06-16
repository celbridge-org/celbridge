namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Manages the user data associated with a Celbridge project.
/// </summary>
public interface IProjectUserData
{
    /// <summary>
    /// Returns the configuration data for the project.
    /// </summary>
    Task<Result<IDataVersion>> GetDataVersionAsync();

    /// <summary>
    /// Updates the configuration data for the project.
    /// </summary>
    Task SetDataVersionAsync(IDataVersion dataVersion);
}
