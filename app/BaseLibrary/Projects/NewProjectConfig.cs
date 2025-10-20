namespace Celbridge.Projects;

public enum NewProjectConfigType
{
    Standard = 0,
    Example
}

/// <summary>
/// Configuration for a new project.
/// </summary>
public record NewProjectConfig(string ProjectFilePath, NewProjectConfigType ConfigType);

