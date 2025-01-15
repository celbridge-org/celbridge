
namespace Celbridge.Forms;

/// <summary>
/// A declarative representation used to define the structure of a form.
/// The visual XAML UI elements are constructed via the FormBuilder service using this definition.
/// </summary>
public interface IForm
{
    /// <summary>
    /// The root panel that contains all the elements of the form.
    /// </summary>
    IFormPanel Panel { get; set; }
}
