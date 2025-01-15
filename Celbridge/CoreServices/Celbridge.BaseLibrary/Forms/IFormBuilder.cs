namespace Celbridge.Forms;

/// <summary>
/// A service that constructs form instances from form definitions.
/// </summary>
public interface IFormBuilder
{
    /// <summary>
    /// Builds a form instance from a form definition.
    /// </summary>
    Result<FormInstance> Build(IForm form);
}
