using Celbridge.Forms;

namespace Celbridge.Entities;

/// <summary>
/// A component editor edits the properties of an entity component.
/// It implements IFormDataProvider, so it is typically used as the data provider for a form.
/// </summary>
public interface IComponentEditor : IFormDataProvider
{
    /// <summary>
    /// Unique identifier for a component editor instance.
    /// </summary>
    Guid EditorId { get; }

    /// <summary>
    /// Returns the component that the editor instance edits.
    /// </summary>
    IComponentProxy Component { get; }

    /// <summary>
    /// Initializes the component editor with the component to be edited.
    /// </summary>
    Result Initialize(IComponentProxy component);

    /// <summary>
    /// Gets the component configuration JSON data.
    /// </summary>
    string GetComponentConfig();

    /// <summary>
    /// Gets the component form configuration JSON data.
    /// </summary>
    string GetComponentForm();

    /// <summary>
    /// Gets the component root form configuration JSON data.
    /// </summary>
    string GetComponentRootForm();

    /// <summary>
    /// Gets summary information for the edited component.
    /// </summary>
    ComponentSummary GetComponentSummary();
}
