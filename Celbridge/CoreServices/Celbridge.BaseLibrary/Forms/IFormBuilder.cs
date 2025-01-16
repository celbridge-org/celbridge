using Celbridge.Entities;

namespace Celbridge.Forms;

/// <summary>
/// A service that constructs form instances from form definitions.
/// </summary>
public interface IFormBuilder
{
    /// <summary>
    /// Builds a form UI instance for editing a component using the specified form definition.
    /// </summary>
    Result<FormInstance> Build(IForm form, ComponentKey component);
}
