using Celbridge.Entities;
using Celbridge.UserInterface.Services.Forms;
using System.Text.Json;

namespace Celbridge.UserInterface.ViewModels.Forms;

public partial class ComboBoxElement : FormElement
{
    public static Result<FrameworkElement> CreateComboBox(JsonElement config, FormBuilder formBuilder)
    {
        var formElement = ServiceLocator.AcquireService<ComboBoxElement>();
        return formElement.Create(config, formBuilder);
    }

    private PropertyBinder<bool>? _isEnabledBinder;

    [ObservableProperty]
    private string _selectedValue = string.Empty;
    private PropertyBinder<string>? _selectedValueBinder;

    [ObservableProperty]
    private List<string> _values = new();
    private PropertyBinder<List<string>>? _valuesBinder;

    protected override Result<FrameworkElement> CreateUIElement(JsonElement config, FormBuilder formBuilder)
    {
        //
        // Create the UI element
        //

        var comboBox = new ComboBox();
        comboBox.DataContext = this;

        //
        // Check all specified config properties are supported
        //

        var validateResult = ValidateConfigKeys(config, new HashSet<string>()
        {
            "isEnabled",
            "header",
            "values",
            "selectedValue"
        });

        if (validateResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail("Invalid ComboBox configuration")
                .WithErrors(validateResult);
        }

        //
        // Apply common config properties
        //

        var commonConfigResult = ApplyCommonConfig(comboBox, config);
        if (commonConfigResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply common config properties")
                .WithErrors(commonConfigResult);
        }

        //
        // Apply element-specific config properties
        //

        var isEnabledResult = ApplyIsEnabledConfig(config, comboBox);
        if (isEnabledResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'isEnabled' config")
                .WithErrors(isEnabledResult);
        }

        var headerResult = ApplyHeaderConfig(config, comboBox);
        if (headerResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'header' config property")
                .WithErrors(headerResult);
        }

        var valuesResult = ApplyValuesConfig(config, comboBox);
        if (valuesResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'values' config property")
                .WithErrors(valuesResult);
        }

        var selectedValueResult = ApplySelectedValueConfig(config, comboBox, formBuilder);
        if (selectedValueResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'selectedValue' config property")
                .WithErrors(selectedValueResult);
        }

        return Result<FrameworkElement>.Ok(comboBox);
    }

    private Result ApplyIsEnabledConfig(JsonElement config, ComboBox button)
    {
        if (config.TryGetProperty("isEnabled", out var configValue))
        {
            if (configValue.IsBindingConfig())
            {
                _isEnabledBinder = PropertyBinder<bool>.Create(button, this)
                    .Setter((value) =>
                    {
                        button.IsEnabled = value;
                    });

                return _isEnabledBinder.Initialize(configValue);
            }
            else if (configValue.ValueKind == JsonValueKind.True)
            {
                button.IsEnabled = true;
            }
            else if (configValue.ValueKind == JsonValueKind.False)
            {
                button.IsEnabled = false;
            }
            else
            {
                return Result<bool>.Fail("'isEnabled' config is not valid");
            }
        }

        return Result.Ok();
    }

    private Result ApplyHeaderConfig(JsonElement config, ComboBox comboBox)
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
            comboBox.Header = header;
        }

        return Result.Ok();
    }

    private Result ApplyValuesConfig(JsonElement config, ComboBox comboBox)
    {
        if (config.TryGetProperty("values", out var configValue))
        {
            if (configValue.ValueKind == JsonValueKind.Array)
            {
                var enumValues = JsonSerializer.Deserialize<List<string>>(configValue.GetRawText());
                if (enumValues is null)
                {
                    return Result.Fail($"Failed to deserialize 'values' property");
                }

                if (enumValues.Count != enumValues.Distinct().Count())
                {
                    return Result.Fail($"'values' property contains duplicate values");
                }

                Values = enumValues;

                return Result.Ok();
            }
            else if (configValue.ValueKind == JsonValueKind.String)
            {
                // If the 'values' property specifies a binding then apply the binding.
                if (configValue.IsBindingConfig())
                {
                    _valuesBinder = PropertyBinder<List<string>>.Create(comboBox, this)
                        .Binding(ComboBox.ItemsSourceProperty, BindingMode.TwoWay, nameof(Values))
                        .Getter(() => Values )
                        .Setter((value) =>
                        {
                            Values = value;
                        });

                    return _valuesBinder.Initialize(configValue);
                }
            }

            return Result.Fail($"Failed to apply binding for 'values' property");
        }

        return Result.Ok();
    }

    private Result ApplySelectedValueConfig(JsonElement config, ComboBox comboBox, FormBuilder formBuilder)
    {
        if (config.TryGetProperty("selectedValue", out var configValue))
        {
            // Check the type
            if (configValue.ValueKind != JsonValueKind.String)
            {
                return Result.Fail("'selectedValue' property must be a string");
            }

            // The ItemSource must be populated before we apply the binding so that the current value
            // can be selected during initialization.

            // Acquire the component that we are binding to.
            var componentEditor = formBuilder.FormDataProvider as IComponentEditor;
            if (componentEditor is null)
            {
                // ComboBox may only be bound to a component property, so the IFormDataProvider
                // must also be an IComponentEditor.
                return Result.Fail("Failed to acquire component for binding 'selectedValue' property");
            }
            var component = componentEditor.Component;

            var propertyPath = configValue.ToString();

            // Attempt to get enum values for the bound property.
            var getEnumResult = component.SchemaReader.GetObjectAttribute<List<string>>("enum", propertyPath);
            if (getEnumResult.IsSuccess)
            {
                var enumValues = getEnumResult.Value;
                if (enumValues.Count != enumValues.Distinct().Count())
                {
                    return Result.Fail($"'selectedValue' property contains duplicate enum values");
                }

                comboBox.ItemsSource = enumValues;
            }
            else
            {
                // Apply the values read from the 'values' config we read earlier.
                // If no values were specified then _values will be empty.
                comboBox.ItemsSource = Values;
            }

            // The ItemsSource has been populated, now setup the property binding.

            if (configValue.IsBindingConfig())
            {
                _selectedValueBinder = PropertyBinder<string>.Create(comboBox, this)
                    .Binding(ComboBox.SelectedValueProperty,
                        BindingMode.TwoWay,
                        nameof(SelectedValue))
                    .Setter((value) =>
                    {
                        SelectedValue = value;
                    })
                    .Getter(() =>
                    {
                        return SelectedValue;
                    });

                return _selectedValueBinder.Initialize(configValue);
            }
            else
            {
                return Result<bool>.Fail("'selectedValue' config does not specify a property binding");
            }
        }

        return Result.Ok();
    }

    protected override void OnFormDataChanged(string propertyPath)
    {
        _isEnabledBinder?.OnFormDataChanged(propertyPath);
        _selectedValueBinder?.OnFormDataChanged(propertyPath);
        _valuesBinder?.OnFormDataChanged(propertyPath);
    }

    protected override void OnMemberDataChanged(string propertyName)
    {
        _selectedValueBinder?.OnMemberDataChanged(propertyName);
        _valuesBinder?.OnMemberDataChanged(propertyName);
    }

    protected override void OnElementUnloaded()
    {
        _isEnabledBinder?.OnElementUnloaded();
        _selectedValueBinder?.OnElementUnloaded();
        _valuesBinder?.OnElementUnloaded();
    }
}
