namespace Celbridge.Entities;

/// <summary>
/// Stores the annotations for the components of an entity.
/// </summary>
public interface IEntityAnnotation
{
    /// <summary>
    /// Initializes the annotation with the number of components in the entity.
    /// </summary>
    void Initialize(int count);

    /// <summary>
    /// The name of the Activity associated with this entity (if any).
    /// </summary>
    string ActivityName { get; set; }

    /// <summary>
    /// Return true if there are any entity or component errors.
    /// The highest priority error is returned in entityError.
    /// </summary>
    bool TryGetError(out AnnotationError? entityError);

    /// <summary>
    /// Returns the list of error messages associated with the entity.
    /// </summary>
    IReadOnlyList<AnnotationError> EntityErrors { get; }

    /// <summary>
    /// Associates an error message with the entity.
    /// </summary>
    void AddEntityError(AnnotationError error);

    /// <summary>
    /// Sets a flag to indicate that the specified component has been recognised by the activity system.
    /// Unrecognized activities are automatically flagged as invalid.
    /// </summary>
    void SetIsRecognized(int componentIndex);

    /// <summary>
    /// Sets the indent level for the specified component in the inspector.
    /// </summary>
    void SetIndent(int componentIndex, int indentLevel);

    /// <summary>
    /// Associates an error message with the specified component.
    /// </summary>
    void AddComponentError(int componentIndex, AnnotationError error);

    /// <summary>
    /// Returns the number of component annotations.
    /// </summary>
    int ComponentAnnotationCount { get; }

    /// <summary>
    /// Returns the annotation data for the specified component.
    /// </summary>
    ComponentAnnotation GetComponentAnnotation(int componentIndex);
}
