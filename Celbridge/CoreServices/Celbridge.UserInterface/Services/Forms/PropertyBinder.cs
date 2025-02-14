using System.Text.Json;
using Celbridge.UserInterface.ViewModels.Forms;

namespace Celbridge.UserInterface.Services.Forms;

public class PropertyBinder
{
    private FrameworkElement _frameworkElement;
    private FormElement _formElement;

    private DependencyProperty? _dependencyProperty;
    private BindingMode _bindingMode = BindingMode.OneTime;
    private string _memberName = string.Empty;
    private bool HasBinding => _dependencyProperty is not null && !string.IsNullOrEmpty(_memberName);

    private Action<string>? _setterAction;
    private Func<string>? _getterAction;

    private string _formPropertyPath = string.Empty;

    private PropertyBinder(FrameworkElement frameworkElement, FormElement formElement)
    {
        _frameworkElement = frameworkElement;
        _formElement = formElement;
    }

    public static PropertyBinder Create(
        FrameworkElement frameworkElement,
        FormElement formElement)
    {
        var propertyBinder = new PropertyBinder(frameworkElement, formElement);
        return propertyBinder;
    }

    public PropertyBinder Binding(
        DependencyProperty dependencyProperty,
        BindingMode bindingMode,
        string memberName)
    {
        _dependencyProperty = dependencyProperty;
        _bindingMode = bindingMode;
        _memberName = memberName;
        return this;
    }

    public PropertyBinder Setter(Action<string> setterAction)
    {
        _setterAction = setterAction;
        return this;
    }

    public PropertyBinder Getter(Func<string> getterAction)
    {
        _getterAction = getterAction;
        return this;
    }

    public Result Initialize(JsonElement configValue)
    {
        if (_setterAction is null)
        {
            return Result.Fail($"No setter action specified");
        }

        // Check the type
        if (configValue.ValueKind != JsonValueKind.String)
        {
            return Result.Fail($"Config value must be a string");
        }

        // Apply the property
        var configText = configValue.GetString()!;
        if (configText.StartsWith('/'))
        {
            // Store the property path for future updates
            _formPropertyPath = configText;

            // Sync the member variable with the form data provider
            SynchonizeProperties();

            if (HasBinding)
            {
                // Bind dependency property to a member variable on the form element class
                _frameworkElement.SetBinding(_dependencyProperty, new Binding()
                {
                    Path = new PropertyPath(_memberName),
                    Mode = _bindingMode
                });
            }

            // Set a flag to indicate that the form element needs to register for
            // property update notifications.
            _formElement.RequiresChangeNotifications = true;
        }
        else
        {
            // Todo: Support localization
            var jsonValue = configValue.GetRawText();
            _setterAction?.Invoke(jsonValue);

            if (HasBinding)
            {
                // Bind dependency property to a member variable on the form element class
                _frameworkElement.SetBinding(_dependencyProperty, new Binding()
                {
                    Path = new PropertyPath(_memberName),
                    Mode = BindingMode.OneTime, // Setting member to a fixed value that will never update
                });
            }
        }

        return Result.Ok();
    }

    public void OnFormDataChanged(string propertyPath)
    {
        if (string.IsNullOrEmpty(_formPropertyPath) ||
            propertyPath != _formPropertyPath)
        {
            return;
        }

        SynchonizeProperties();
    }

    public void OnMemberDataChanged(string propertyName)
    {
        if (_getterAction is null ||
            string.IsNullOrEmpty(_memberName) ||
            propertyName != _memberName)
        {
            return;
        }

        var jsonValue = _getterAction.Invoke();

        var formDataProvider = _formElement.FormDataProvider;
        formDataProvider.SetProperty(_formPropertyPath, jsonValue, false);
    }

    private Result SynchonizeProperties()
    {
        if (_setterAction is null)
        {
            return Result.Fail($"No setter action specified");
        }

        var formDataProvider = _formElement.FormDataProvider;

        // Read the current property JSON value via the FormDataProvider
        var getResult = formDataProvider.GetProperty(_formPropertyPath);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Failed to get property: '{_formPropertyPath}'")
                .WithErrors(getResult);
        }
        var jsonValue = getResult.Value;

        // Update the member variable
        _setterAction.Invoke(jsonValue);

        return Result.Ok();
    }
}
