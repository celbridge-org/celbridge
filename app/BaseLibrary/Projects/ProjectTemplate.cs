namespace Celbridge.Projects;

/// <summary>
/// Defines a project template that can be used to create new projects.
/// Uses naming conventions to automatically construct localization keys and asset paths from the Id.
/// </summary>
public record ProjectTemplate
{
    /// <summary>
    /// Unique identifier for the template (e.g., "Empty", "Examples").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display name shown in the template selector.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Description shown when the template is selected.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Icon glyph for the template.
    /// </summary>
    public required string Icon { get; init; }

    /// <summary>
    /// Gets the path to the zip asset file containing the template content.
    /// </summary>
    public string TemplateAssetPath => $"ms-appx:///Assets/Templates/{Id}.zip";
}
