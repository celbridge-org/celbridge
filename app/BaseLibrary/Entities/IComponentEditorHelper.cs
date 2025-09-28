namespace Celbridge.Entities;

/// <summary>
/// Provides supporting functionality for ComponentEditorBase.
/// Internally, ComponentEditorHelper uses other services via the DI system to implement all the
/// required functionality. This simplifies the design of ComponentEditorBase as it only depends on
/// this one interface.
/// </summary>
public interface IComponentEditorHelper
{
    /// <summary>
    /// Initializes the helper with the component to be edited.
    /// </summary>
    /// <param name="component"></param>
    Result Initialize(IComponentProxy component);

    /// <summary>
    /// Called when the component editor is shutting down.
    /// </summary>
    void Uninitialize();

    /// <summary>
    /// The component being edited.
    /// </summary>
    IComponentProxy Component { get; }

    /// <summary>
    /// An event that is fired when a component property changes
    /// </summary>
    event Action<string>? ComponentPropertyChanged;
}
