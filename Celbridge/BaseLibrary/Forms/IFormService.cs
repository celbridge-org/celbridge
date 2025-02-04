namespace Celbridge.Forms;

/// <summary>
/// A service for creating forms.
/// Forms are UI elements used to edit the properties of an object, such as an entity component.
/// </summary>
public interface IFormService
{
    /// <summary>
    /// Create form based on a JSON form config and a data provider.
    /// The form name is used when reporting errors, it does not have any other function.
    /// </summary>
    Result<object> CreateForm(string formName, string formConfig, IFormDataProvider formDataProvider);
}
