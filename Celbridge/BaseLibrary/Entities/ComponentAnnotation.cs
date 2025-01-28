namespace Celbridge.Entities;

/// <summary>
/// Annotation data that controls how a component is displayed in the inspector.
/// </summary>
public record ComponentAnnotation(int IndentLevel, List<ComponentError> Errors);
