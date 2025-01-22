namespace Celbridge.Forms;

/// <summary>
/// A service that constructs form UI elements based on a JSON definition.
/// </summary>
public interface IFormBuilder
{
    /// <summary>
    /// Constructs a form UI element based on a JSON configuration and a form data provider.
    /// </summary>
    Result<object> BuildForm(string formName, string fromConfig, IFormDataProvider formDataProvider);
}
