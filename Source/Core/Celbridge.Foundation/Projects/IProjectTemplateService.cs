namespace Celbridge.Projects;

/// <summary>
/// Provides access to available project templates and project creation from templates.
/// </summary>
public interface IProjectTemplateService
{
    /// <summary>
    /// Returns all available project templates.
    /// </summary>
    IReadOnlyList<ProjectTemplate> GetTemplates();

    /// <summary>
    /// Returns the default template (Empty Project).
    /// </summary>
    ProjectTemplate GetDefaultTemplate();

    /// <summary>
    /// Returns the names of the files the template writes that already exist in the destination
    /// folder, sorted by name.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> GetConflictingFileNamesAsync(string projectFilePath, ProjectTemplate template);

    /// <summary>
    /// Creates a new project from a template at the specified path. Fails without writing anything
    /// when the destination folder already holds one of the files the template writes, unless
    /// replaceExistingFiles asks for those files to be replaced. An existing .gitignore is always
    /// merged, never replaced.
    /// </summary>
    Task<Result> CreateFromTemplateAsync(string projectFilePath, ProjectTemplate template, bool replaceExistingFiles = false);
}
