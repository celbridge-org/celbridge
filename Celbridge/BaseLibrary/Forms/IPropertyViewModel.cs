namespace Celbridge.Forms;

/// <summary>
/// A view model that binds a form UI element to a form property via a form data provider.
/// </summary>
public interface IPropertyViewModel
{
    /// <summary>
    /// The name of the member variable on the view model class to bind to.
    /// </summary>
    string BoundPropertyName { get; }

    /// <summary>
    /// Initializes the view model with the form data provider and the property path.
    /// </summary>
    Result Initialize(IFormDataProvider formDataProvider, string propertyPath);

    /// <summary>
    /// A callback called when the view is unloaded.
    /// </summary>
    void OnViewUnloaded();
}
