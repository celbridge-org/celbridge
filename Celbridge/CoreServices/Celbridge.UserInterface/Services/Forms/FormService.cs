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
            var ensureResult = EnsureValidFormConfig(formConfig);
            if (ensureResult.IsFailure)
            {
                return Result<object>.Fail($"Failed to create form: '{formName}'")
                    .WithErrors(ensureResult);
            }
            var config = ensureResult.Value;

            var document = JsonDocument.Parse(config);
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

    private Result<string> EnsureValidFormConfig(string formConfig)
    {
        if (string.IsNullOrEmpty(formConfig))
        {
            return Result<string>.Fail($"Form config is empty");
        }

        // Get the first non-whitespace character
        int index = formConfig.AsSpan().IndexOfAnyExcept(" \t\r\n");

        if (index < 0)
        {
            return Result<string>.Fail($"Form config contains only whitespace");
        }

        char firstChar = formConfig[index];
        switch (firstChar)
        {
            case '{':
                // Form elements are already wrapped in an object, return unmodified.
                return Result<string>.Ok(formConfig);

            case '[':
                {
                    // Wrap the form elements in a vertical stack panel object
                    var wrappedConfig = $$"""
                        {
                            "element": "StackPanel",
                            "orientation": "Vertical",
                            "children": {{formConfig}}
                        }
                        """;
                    return Result<string>.Ok(wrappedConfig);
                }

            default:
                return Result<string>.Fail($"Form config root element must be an object or an array");
        }
    }
}
