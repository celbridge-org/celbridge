namespace Celbridge.Entities;

/// <summary>
/// Error states for reporting component configuration issues.
/// </summary>
public enum ComponentErrorSeverity
{
    /// <summary>
    /// A major issue that should be addressed immediately.
    /// </summary>
    Critical,

    /// <summary>
    /// An issue that prevents the component from functioning.
    /// </summary>
    Error,

    /// <summary>
    /// An issue that may result in unexpected behaviour.
    /// </summary>
    Warning
}

/// <summary>
/// Describes an error condition for a component.
/// </summary>
public record ComponentError(ComponentErrorSeverity Severity, string Message, string Description);
