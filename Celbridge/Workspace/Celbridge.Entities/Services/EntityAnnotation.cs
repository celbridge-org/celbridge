using Celbridge.Entities;

namespace Celbridge.Activities.Services;

public class EntityAnnotation : IEntityAnnotation
{
    private readonly HashSet<int> _recognizedComponents = new();
    private readonly List<ComponentAnnotation> _annotations = new();
    private readonly ComponentAnnotation _invalidAnnotation;

    public EntityAnnotation()
    {
        // Create a default invalid annotation.
        // This is returned for any unrecognized components.
        List<ComponentError> errors = new()
        {
            new ComponentError(
                ComponentErrorSeverity.Critical, 
                "Invalid component", 
                "This component is not valid in this position.")
        };

        _invalidAnnotation = new ComponentAnnotation(0, errors);
    }

    public void Initialize(int count)
    {
        _annotations.Clear();
        _recognizedComponents.Clear();

        // Populate the list with empty annotations.
        for (int i = 0; i < count; i++)
        {
            var annotation = new ComponentAnnotation(0, new());
            _annotations.Add(annotation);
        }
    }

    public int Count => _annotations.Count;

    public void SetIsRecognized(int componentIndex)
    {
        if (componentIndex < 0 || componentIndex >= _annotations.Count)
        {
            throw new IndexOutOfRangeException($"Component index is out of range: {componentIndex}");
        }

        _recognizedComponents.Add(componentIndex);
    }

    public void SetIndent(int componentIndex, int indentLevel)
    {
        if (componentIndex < 0 || componentIndex >= _annotations.Count)
        {
            throw new IndexOutOfRangeException($"Component index is out of range: {componentIndex}");
        }

        var updatedAnnotation = _annotations[componentIndex] with
        {
            IndentLevel = indentLevel
        };

        _annotations[componentIndex] = updatedAnnotation;
    }

    public void AddError(int componentIndex, ComponentError error)
    {
        if (componentIndex < 0 || componentIndex >= _annotations.Count)
        {
            throw new IndexOutOfRangeException($"Component index is out of range: {componentIndex}");
        }

        var annotation = _annotations[componentIndex];

        var errorList = annotation.Errors;
        errorList.Add(error);

        // Ensure the error list is always sorted in a stable order, with the 
        // highest severity errors listed first.
        errorList.Sort((a, b) =>
        {
            if (a.Severity == b.Severity)
            {
                // Sort errors of the same severity alphabetically by their message.
                return string.Compare(a.Message, b.Message, StringComparison.Ordinal);
            }

            // Sort errors by severity
            return (int)a.Severity - (int)b.Severity;
        });
    }

    public ComponentAnnotation GetComponentAnnotation(int componentIndex)
    {
        if (componentIndex < 0 || componentIndex >= _annotations.Count)
        {
            throw new IndexOutOfRangeException($"Component index is out of range: {componentIndex}");
        }

        if (!_recognizedComponents.Contains(componentIndex))
        {
            // Unrecognized components are annotated as invalid.
            return _invalidAnnotation;
        }

        var annotation = _annotations[componentIndex];
        return annotation;
    }
}
