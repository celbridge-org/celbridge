using System.Text.Json;
using Celbridge.UserInterface.ViewModels.Forms;

namespace Celbridge.UserInterface.Services.Forms;

public class PropertyBinder<T> where T : notnull
{
    private FrameworkElement _frameworkElement;
    private FormElement _formElement;

    private DependencyProperty? _dependencyProperty;
    private BindingMode _bindingMode = BindingMode.OneTime;
    private string _memberName = string.Empty;
    private bool HasBinding => _dependencyProperty is not null && !string.IsNullOrEmpty(_memberName);

    private Action<T>? _setterAction;
    private Func<T>? _getterAction;

    private string _formPropertyPath = string.Empty;

    private PropertyBinder(FrameworkElement frameworkElement, FormElement formElement)
    {
        _frameworkElement = frameworkElement;
        _formElement = formElement;
    }

    public static PropertyBinder<T> Create(
        FrameworkElement frameworkElement,
        FormElement formElement)
    {
        return new PropertyBinder<T>(frameworkElement, formElement);
    }

    public PropertyBinder<T> Binding(
        DependencyProperty dependencyProperty,
        BindingMode bindingMode,
        string memberName)
    {
        _dependencyProperty = dependencyProperty;
        _bindingMode = bindingMode;
        _memberName = memberName;
        return this;
    }

    public PropertyBinder<T> Setter(Action<T> setterAction)
    {
        _setterAction = setterAction;
        return this;
    }

    public PropertyBinder<T> Getter(Func<T> getterAction)
    {
        _getterAction = getterAction;
        return this;
    }

    public Result Initialize(JsonElement configValue)
    {
        if (!configValue.IsBindingConfig())
        {
            return Result.Fail("Invalid binding configuration");
        }

        if (_setterAction is null)
        {
            return Result.Fail("No setter action specified");
        }

        _formPropertyPath = configValue.GetString()!;
        var syncResult = SynchonizeProperties();
        if (syncResult.IsFailure)
        {
            return Result.Fail("Failed to synchonize properties")
                .WithErrors(syncResult);
        }

        if (HasBinding)
        {
            _frameworkElement.SetBinding(_dependencyProperty, new Binding()
            {
                Path = new PropertyPath(_memberName),
                Mode = _bindingMode
            });
        }

        _formElement.RequiresChangeNotifications = true;

        return Result.Ok();
    }

    public void OnFormDataChanged(string propertyPath)
    {
        if (!string.IsNullOrEmpty(_formPropertyPath) && propertyPath == _formPropertyPath)
        {
            SynchonizeProperties();
        }
    }

    public void OnMemberDataChanged(string propertyName)
    {
        if (_getterAction is null || 
            string.IsNullOrEmpty(_memberName) || 
            propertyName != _memberName)
        {
            return;
        }

        T value = _getterAction.Invoke();
        string jsonValue = JsonSerializer.Serialize(value);
        _formElement.FormDataProvider.SetProperty(_formPropertyPath, jsonValue, false);
    }

    public void OnElementUnloaded()
    {
        _setterAction = null;
        _getterAction = null;
    }

    private Result SynchonizeProperties()
    {
        if (_setterAction is null)
        {
            return Result.Fail("No setter action specified");
        }

        var formDataProvider = _formElement.FormDataProvider;
        var getResult = formDataProvider.GetProperty(_formPropertyPath);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Failed to get property: '{_formPropertyPath}'")
                .WithErrors(getResult);
        }

        try
        {
            T value = JsonSerializer.Deserialize<T>(getResult.Value) ?? throw new JsonException("Deserialization resulted in null");
            _setterAction.Invoke(value);
        }
        catch (JsonException ex)
        {
            return Result.Fail($"Failed to deserialize JSON: {ex.Message}");
        }

        return Result.Ok();
    }
}
