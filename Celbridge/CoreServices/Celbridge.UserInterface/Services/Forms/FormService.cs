using System.Text.Json;
using Celbridge.Forms;

namespace Celbridge.UserInterface.Services.Forms;

public class FormService : IFormService
{
    private readonly FormRegistry _formRegistry;
    private readonly FormBuilder _formBuilder;

    public FormService(
        FormRegistry formRegistry,
        FormBuilder formBuilder)
    {
        _formRegistry = formRegistry;
        _formBuilder = formBuilder;
    }

    public Result RegisterForm(string formName, string formConfigJSON, FormScope scope)
    {
        return _formRegistry.RegisterFormConfig(formName, formConfigJSON, scope);
    }

    public Result<object> CreateForm(string formName, IFormDataProvider formDataProvider)
    {
        // Get the previously registered form config
        var getConfigResult = _formRegistry.GetFormConfig(formName);
        if (getConfigResult.IsFailure)
        {
            return Result<object>.Fail($"Failed to get form: '{formName}'")
                .WithErrors(getConfigResult);
        }
        var formConfig = getConfigResult.Value;

        // Build an instance of the form, binding to the formDataProvider
        var buildResult = _formBuilder.BuildForm(formName, formConfig, formDataProvider);
        if (buildResult.IsFailure)
        {
            return Result<object>.Fail($"Failed to build form: '{formName}'")
                .WithErrors(buildResult);
        }
        var formPanel = buildResult.Value;

        return Result<object>.Ok(formPanel);
    }
}
