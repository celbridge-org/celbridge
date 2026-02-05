using Celbridge.Entities;

namespace Celbridge.Inspector;

/// <summary>
/// The inspector service provides functionality to support the inspector panel in the workspace UI.
/// </summary>
public interface IInspectorService
{
    /// <summary>
    /// The current resource being inspected.
    /// </summary>
    ResourceKey InspectedResource { get; }

    /// <summary>
    /// The index of the current component being inspected.
    /// -1 if no component is currently being inspected.
    /// </summary>
    int InspectedComponentIndex { get; }

    /// <summary>
    /// Set the editing mode for the component panel.
    /// </summary>
    ComponentPanelMode ComponentPanelMode { get; set; }

    /// <summary>
    /// Gets or sets the inspector panel view.
    /// </summary>
    IInspectorPanel InspectorPanel { get; set; }

    /// <summary>
    /// Returns the factory used to create inspector UI elements. 
    /// </summary>
    IInspectorFactory InspectorFactory { get; }

    /// <summary>
    /// Updates the inspector service.
    /// </summary>
    Task<Result> UpdateAsync();

    /// <summary>
    /// Creates a form UI element to edit a component via a component editor instance.
    /// </summary>
    Result<object> CreateComponentEditorForm(IComponentEditor componentEditor);

    /// <summary>
    /// Acquire a component editor for the specified component.
    /// The component key must be for the currently inspected resource.
    /// This method will cache the editor for subsequent requests.
    /// </summary>
    Result<IComponentEditor> AcquireComponentEditor(ComponentKey component);

    /// <summary>
    /// Returns the most recently generated entity annotation for the specified resource.
    /// </summary>
    Result<IEntityAnnotation> GetCachedEntityAnnotation(ResourceKey resource);
}
