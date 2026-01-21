namespace Celbridge.Projects;

/// <summary>
/// Defines a project template that can be used to create new projects.
/// </summary>
public record ProjectTemplate
{
    /// <summary>
    /// Unique identifier for the template.
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
    /// Path to the zip asset file containing the template content.
    /// All templates must have a zip file.
    /// Example: "ms-appx:///Assets/Templates/Empty.zip"
    /// </summary>
    public required string TemplateAssetPath { get; init; }

    /// <summary>
    /// The name of the .celbridge file inside the zip (before renaming to user's project name).
    /// </summary>
    public required string TemplateProjectFileName { get; init; }
}
