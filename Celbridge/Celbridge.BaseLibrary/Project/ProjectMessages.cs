namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Sent when the project service has been initialized with the loaded project data.
/// </summary>
public record ProjectServiceCreatedMessage(IProjectData ProjectData);
