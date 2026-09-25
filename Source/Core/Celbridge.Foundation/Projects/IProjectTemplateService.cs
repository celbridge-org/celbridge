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
    /// folder, sorted by name. Creating the project replaces these files, so callers are expected
    /// to confirm the replacement with the user first.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> GetConflictingFileNamesAsync(string projectFilePath, ProjectTemplate template);

    /// <summary>
    /// Creates a new project from a template at the specified path. Files the template writes
    /// replace any file of the same name already in the destination folder. An existing .gitignore
    /// is merged rather than replaced, gaining only the patterns it was missing.
    /// </summary>
    Task<Result> CreateFromTemplateAsync(string projectFilePath, ProjectTemplate template);
}
