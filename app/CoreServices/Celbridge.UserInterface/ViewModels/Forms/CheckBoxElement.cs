using Celbridge.UserInterface.Services.Forms;
using System.Text.Json;

namespace Celbridge.UserInterface.ViewModels.Forms;

public partial class CheckBoxElement : FormElement
{
    public static Result<FrameworkElement> CreateCheckBox(JsonElement config, FormBuilder formBuilder)
    {
        var formElement = ServiceLocator.AcquireService<CheckBoxElement>();
        return formElement.Create(config, formBuilder);
    }

    [ObservableProperty]
    private bool _isEnabled = true;
    private PropertyBinder<bool>? _isEnabledBinder;

    [ObservableProperty]
    private bool _isChecked = false;
    private PropertyBinder<bool>? _isCheckedBinder;

    protected override Result<FrameworkElement> CreateUIElement(JsonElement config, FormBuilder formBuilder)
    {
        //
        // Create the UI element
        //

        var checkBox = new CheckBox();
        checkBox.DataContext = this;

        //
        // Check all specified config properties are supported
        //

        var validateResult = ValidateConfigKeys(config, new HashSet<string>()
        {
            "isEnabled",
            "header",
            "isChecked"
        });

        if (validateResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail("Invalid CheckBox configuration")
                .WithErrors(validateResult);
        }

        //
        // Apply common config properties
        //

        var commonConfigResult = ApplyCommonConfig(checkBox, config);
        if (commonConfigResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply common config properties")
                .WithErrors(commonConfigResult);
        }

        //
        // Apply element-specific config properties
        //

        var isEnabledResult = ApplyIsEnabledConfig(config, checkBox);
        if (isEnabledResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'isEnabled' config")
                .WithErrors(isEnabledResult);
        }

        var headerResult = ApplyHeaderConfig(config, checkBox);
        if (headerResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'header' config property")
                .WithErrors(headerResult);
        }

        var isCheckedResult = ApplyIsCheckedConfig(config, checkBox);
        if (isCheckedResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'isChecked' config property")
                .WithErrors(isCheckedResult);
        }

        return Result<FrameworkElement>.Ok(checkBox);
    }

    private Result ApplyIsEnabledConfig(JsonElement config, CheckBox checkBox)
    {
        if (config.TryGetProperty("isEnabled", out var configValue))
        {
            if (configValue.IsBindingConfig())
            {
                _isEnabledBinder = PropertyBinder<bool>.Create(checkBox, this)
                    .Binding(CheckBox.IsEnabledProperty, BindingMode.OneWay, nameof(IsEnabled))
                    .Setter((value) =>
                    {
                        IsEnabled = value;
                    });

                return _isEnabledBinder.Initialize(configValue);
            }
            else if (configValue.ValueKind == JsonValueKind.True)
            {
                checkBox.IsEnabled = true;
            }
            else if (configValue.ValueKind == JsonValueKind.False)
            {
                checkBox.IsEnabled = false;
            }
            else
            {
                return Result.Fail("'isEnabled' config is not valid");
            }
        }

        return Result.Ok();
    }

    private Result ApplyHeaderConfig(JsonElement config, CheckBox checkBox)
    {
        if (config.TryGetProperty("header", out var jsonValue))
        {
            // Check the type
            if (jsonValue.ValueKind != JsonValueKind.String)
            {
                return Result.Fail("'header' property must be a string");
            }

            // Todo: Support binding

            // Apply the property
            var header = jsonValue.GetString() ?? string.Empty;
            checkBox.Content = header;
        }

        return Result.Ok();
    }

    private Result ApplyIsCheckedConfig(JsonElement config, CheckBox checkBox)
    {
        if (config.TryGetProperty("isChecked", out var configValue))
        {
            if (configValue.IsBindingConfig())
            {
                _isCheckedBinder = PropertyBinder<bool>.Create(checkBox, this)
                    .Binding(CheckBox.IsCheckedProperty, BindingMode.TwoWay, nameof(IsChecked))
                    .Setter((value) =>
                    {
                        IsChecked = value;
                    })
                    .Getter(() =>
                    {
                        return IsChecked;
                    });

                return _isCheckedBinder.Initialize(configValue);
            }
            else if (configValue.ValueKind == JsonValueKind.True)
            {
                checkBox.IsChecked = true;
            }
            else if (configValue.ValueKind == JsonValueKind.False)
            {
                checkBox.IsChecked = false;
            }
            else
            {
                return Result.Fail("'isChecked' config is not valid");
            }
        }

        return Result.Ok();
    }

    protected override void OnFormDataChanged(string propertyPath)
    {
        _isEnabledBinder?.OnFormDataChanged(propertyPath);
        _isCheckedBinder?.OnFormDataChanged(propertyPath);
    }

    protected override void OnMemberDataChanged(string propertyName)
    {
        _isEnabledBinder?.OnMemberDataChanged(propertyName);
        _isCheckedBinder?.OnMemberDataChanged(propertyName);
    }

    protected override void OnElementUnloaded()
    {
        _isEnabledBinder?.OnElementUnloaded();
        _isCheckedBinder?.OnElementUnloaded();
    }
}
