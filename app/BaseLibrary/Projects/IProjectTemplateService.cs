namespace Celbridge.Projects;

/// <summary>
/// Provides access to available project templates.
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
}
