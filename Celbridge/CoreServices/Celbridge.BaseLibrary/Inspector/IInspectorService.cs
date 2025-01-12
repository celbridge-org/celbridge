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
    /// Returns the inspector panel view.
    /// </summary>
    IInspectorPanel InspectorPanel { get; }

    /// <summary>
    /// Returns the factory used to create inspector UI elements. 
    /// </summary>
    IInspectorFactory InspectorFactory { get; }

    /// <summary>
    /// A service for creating field UI elements for editing component values.
    /// </summary>
    IFieldFactory FieldFactory { get; }

    /// <summary>
    /// Updates the inspector service.
    /// </summary>
    Task<Result> UpdateAsync();
}
