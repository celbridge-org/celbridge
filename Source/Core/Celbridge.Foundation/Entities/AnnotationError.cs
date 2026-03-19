namespace Celbridge.Entities;

/// <summary>
/// Entity annotation error severity level.
/// </summary>
public enum AnnotationErrorSeverity
{
    /// <summary>
    /// An issue that may result in unexpected behaviour.
    /// </summary>
    Warning,

    /// <summary>
    /// An issue that prevents the component from functioning.
    /// </summary>
    Error,

    /// <summary>
    /// An critical issue that should be addressed as a priority.
    /// </summary>
    Critical
}

/// <summary>
/// Describes a single configuration error for an entity annotation.
/// </summary>
public record AnnotationError(AnnotationErrorSeverity Severity, string Message, string Description);
