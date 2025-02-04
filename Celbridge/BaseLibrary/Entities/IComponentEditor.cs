using Celbridge.Forms;

namespace Celbridge.Entities;

/// <summary>
/// A component editor edits the properties of an entity component.
/// It implements IFormDataProvider, so it is typically used as the data provider for a form.
/// </summary>
public interface IComponentEditor : IFormDataProvider
{
    /// <summary>
    /// Returns the component that the editor instance edits.
    /// </summary>
    IComponentProxy Component { get; }

    /// <summary>
    /// Initializes the component editor with the component to be edited.
    /// </summary>
    Result Initialize(IComponentProxy component);

    /// <summary>
    /// Gets the form configuration data for the component.
    /// </summary>
    public abstract string GetComponentForm();

    /// <summary>
    /// Gets the configuration data for the component.
    /// </summary>
    string GetComponentConfig();

    /// <summary>
    /// Gets summary information for the edited component.
    /// </summary>
    ComponentSummary GetComponentSummary();
}
