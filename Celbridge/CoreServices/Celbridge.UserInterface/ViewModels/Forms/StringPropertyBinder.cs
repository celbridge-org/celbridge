using System.Text.Json;
using System.Text.Json.Nodes;
using Celbridge.Forms;

namespace Celbridge.UserInterface.ViewModels.Forms;

public class StringPropertyBinder
{
    private string _propertyPath = string.Empty;
    public string PropertyPath => _propertyPath;

    private IFormDataProvider? _formDataProvider;

    private Action<string>? _setProperty;

    public Result Initialize(
        FormElement formElement,
        FrameworkElement frameworkElement,
        JsonElement config,
        string configKey,
        DependencyProperty dependencyProperty,
        BindingMode bindingMode,
        string memberPropertyName,
        Action<string> setMemberProperty)
    {
        if (config.TryGetProperty(configKey, out var stringValue))
        {
            // Check the type
            if (stringValue.ValueKind != JsonValueKind.String)
            {
                return Result.Fail($"'{configKey}' property must be a string");
            }

            // Apply the property
            var stringText = stringValue.GetString()!;
            if (stringText.StartsWith('/'))
            {
                _formDataProvider = formElement.FormDataProvider;
                _setProperty = setMemberProperty;

                // Store the property path for future updates
                _propertyPath = stringText;

                // Bind dependency property to a member variable on this class
                frameworkElement.SetBinding(dependencyProperty, new Binding()
                {
                    Path = new PropertyPath(memberPropertyName),
                    Mode = bindingMode
                });

                // Sync the member variable with the form data provider
                UpdatePropertyValue();

                // Set a flag to indicate that this element has bindings
                formElement.HasBindings = true;

                return Result.Ok();
            }
            else
            {
                // Set member property directly (i.eno binding)
                // Todo: Support localization
                setMemberProperty.Invoke(stringText);
            }
        }

        return Result.Ok();
    }

    public Result UpdatePropertyValue()
    {
        Guard.IsNotNull(_formDataProvider);

        // Read the current property JSON value via the FormDataProvider
        var getResult = _formDataProvider.GetProperty(_propertyPath);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Failed to get property: '{_propertyPath}'")
                .WithErrors(getResult);
        }
        var jsonValue = getResult.Value;

        // Parse the JSON value as a string
        var jsonNode = JsonNode.Parse(jsonValue);
        if (jsonNode is null)
        {
            return Result.Fail($"Failed to parse JSON value for property: '{_propertyPath}'");
        }
        var value = jsonNode.ToString();

        // Update the member variable
        _setProperty?.Invoke(value);

        return Result.Ok();
    }
}
