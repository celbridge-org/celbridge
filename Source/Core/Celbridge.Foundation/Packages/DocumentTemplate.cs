namespace Celbridge.Packages;

/// <summary>
/// A document template declared by a package.
/// Templates provide starter content for new files of the package's file type.
/// </summary>
public partial record DocumentTemplate
{
    /// <summary>
    /// Unique identifier for this template within the package.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display name or localization key for the template.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Path to the template file, relative to the package folder.
    /// </summary>
    public string TemplateFile { get; init; } = string.Empty;

    /// <summary>
    /// If true, this template is used for programmatic file creation.
    /// </summary>
    public bool Default { get; init; }
}
