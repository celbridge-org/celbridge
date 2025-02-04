using System.Text.Json;
using Celbridge.Forms;

namespace Celbridge.UserInterface.Services.Forms;

public class FormService : IFormService
{
    private readonly FormBuilder _formBuilder;

    public FormService(
        FormBuilder formBuilder)
    {
        _formBuilder = formBuilder;
    }

    public Result<object> CreateForm(string formName, string formConfig, IFormDataProvider formDataProvider)
    {
        try
        {
            var document = JsonDocument.Parse(formConfig);
            var formConfigElement = document.RootElement;

            // Build an instance of the form, binding to the formDataProvider
            var buildResult = _formBuilder.BuildForm(formName, formConfigElement, formDataProvider);
            if (buildResult.IsFailure)
            {
                return Result<object>.Fail($"Failed to build form: '{formName}'")
                    .WithErrors(buildResult);
            }
            var formPanel = buildResult.Value;

            return Result<object>.Ok(formPanel);
        }
        catch (Exception ex)
        {
            return Result<object>.Fail($"Failed to create form: '{formName}'")
                .WithException(ex);
        }
    }
}
