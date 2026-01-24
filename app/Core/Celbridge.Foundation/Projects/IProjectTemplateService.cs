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
    /// Creates a new project from a template at the specified path.
    /// </summary>
    Task<Result> CreateFromTemplateAsync(string projectFilePath, ProjectTemplate template);
}
