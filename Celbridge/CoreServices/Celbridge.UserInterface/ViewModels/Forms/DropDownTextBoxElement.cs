using Celbridge.UserInterface.Services.Forms;
using Celbridge.UserInterface.Views.Controls;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using Windows.System;

namespace Celbridge.UserInterface.ViewModels.Forms;

public partial class DropDownTextBoxElement : FormElement
{
    public static Result<FrameworkElement> CreateDropDownTextBox(JsonElement config, FormBuilder formBuilder)
    {
        var formElement = ServiceLocator.AcquireService<DropDownTextBoxElement>();
        return formElement.Create(config, formBuilder);
    }

    [ObservableProperty]
    private bool _isEnabled = true;
    private PropertyBinder<bool>? _isEnabledBinder;

    [ObservableProperty]
    private string _text = string.Empty;
    private PropertyBinder<string>? _textBinder;

    [ObservableProperty]
    private string _placeholderText = string.Empty;
    private PropertyBinder<string>? _placeholderTextBinder;

    [ObservableProperty]
    private string _buttonText = string.Empty;
    private PropertyBinder<string>? _buttonTextBinder;

    [ObservableProperty]
    private List<string> _values = new();
    private PropertyBinder<List<string>>? _valuesBinder;

    private bool _autoTrim = true;

    protected override Result<FrameworkElement> CreateUIElement(JsonElement config, FormBuilder formBuilder)
    {
        //
        // Create the UI element
        //

        var dropDownTextBox = new DropDownTextBox();
        dropDownTextBox.DataContext = this;

        // Add a horizontal panel for the button content
        var buttonPanel = new StackPanel();
        buttonPanel.Orientation = Orientation.Horizontal;
        dropDownTextBox.InnerButton.Content = buttonPanel;

        dropDownTextBox.KeyDown += (sender, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                // Pressing enter moves focus to next focusable element
                var options = new FindNextElementOptions
                {
                    SearchRoot = ((UIElement)sender).XamlRoot!.Content
                };

                FocusManager.TryMoveFocus(FocusNavigationDirection.Next, options);

                e.Handled = true;
            }
        };

        //
        // Check all specified config properties are supported
        //

        var validateResult = ValidateConfigKeys(config, new HashSet<string>()
        {
            "isEnabled",
            "text",
            "header",
            "placeholderText",
            "autoTrim",
            "buttonIcon",
            "buttonText",
            "values"
        });

        if (validateResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail("Invalid form element configuration")
                .WithErrors(validateResult);
        }

        //
        // Apply common element config properties
        //

        var commonConfigResult = ApplyCommonConfig(dropDownTextBox, config);
        if (commonConfigResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply common config properties")
                .WithErrors(commonConfigResult);
        }

        //
        // Apply element-specific config properties
        //

        var isEnabledResult = ApplyIsEnabledConfig(config, dropDownTextBox);
        if (isEnabledResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'isEnabled' config")
                .WithErrors(isEnabledResult);
        }

        var headerResult = ApplyHeaderConfig(config, dropDownTextBox);
        if (headerResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'header' config property")
                .WithErrors(headerResult);        
        }

        var placeholderResult = ApplyPlaceholderTextConfig(config, dropDownTextBox);
        if (placeholderResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'placeholderText' config property")
                .WithErrors(placeholderResult);
        }

        var textResult = ApplyTextConfig(config, dropDownTextBox);
        if (textResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'text' config property")
                .WithErrors(textResult);
        }

        var iconResult = ApplyButtonIconConfig(config, buttonPanel);
        if (iconResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'buttonIcon' config property")
                .WithErrors(iconResult);
        }

        var buttonTextResult = ApplyButtonTextConfig(config, buttonPanel);
        if (buttonTextResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'buttonText' config property")
                .WithErrors(buttonTextResult);
        }        

        var valuesResult = ApplyValuesConfig(config, dropDownTextBox);
        if (valuesResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'values' config property")
                .WithErrors(valuesResult);
        }

        var autoTrimResult = ApplyAutoTrimConfig(config, dropDownTextBox);
        if (autoTrimResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'autoTrim' config property")
                .WithErrors(autoTrimResult);
        }

        return Result<FrameworkElement>.Ok(dropDownTextBox);
    }

    private Result ApplyIsEnabledConfig(JsonElement config, DropDownTextBox dropDownTextBox)
    {
        if (config.TryGetProperty("isEnabled", out var configValue))
        {
            if (configValue.IsBindingConfig())
            {
                _isEnabledBinder = PropertyBinder<bool>.Create(dropDownTextBox, this)
                    .Binding(UserControl.IsEnabledProperty, BindingMode.OneWay, nameof(IsEnabled))
                    .Setter((value) =>
                    {
                        IsEnabled = value;
                    });

                return _isEnabledBinder.Initialize(configValue);
            }
            else if (configValue.ValueKind == JsonValueKind.True)
            {
                dropDownTextBox.IsEnabled = true;
            }
            else if (configValue.ValueKind == JsonValueKind.False)
            {
                dropDownTextBox.IsEnabled = false;
            }
            else
            {
                return Result<bool>.Fail("'isEnabled' config is not valid");
            }
        }

        return Result.Ok();
    }

    private Result ApplyHeaderConfig(JsonElement config, DropDownTextBox dropDownTextBox)
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
            dropDownTextBox.InnerTextBox.Header = header;
        }

        return Result.Ok();
    }

    private Result ApplyPlaceholderTextConfig(JsonElement config, DropDownTextBox dropDownTextBox)
    {
        if (config.TryGetProperty("placeholderText", out var configValue))
        {
            if (configValue.IsBindingConfig())
            {
                _placeholderTextBinder = PropertyBinder<string>.Create(dropDownTextBox.InnerTextBox, this)
                .Binding(TextBox.PlaceholderTextProperty, BindingMode.OneWay, nameof(PlaceholderText))
                .Setter((value) =>
                {
                    PlaceholderText = value;
                });

                return _placeholderTextBinder.Initialize(configValue);
            }
            else if (configValue.ValueKind == JsonValueKind.String)
            {
                dropDownTextBox.InnerTextBox.PlaceholderText = configValue.GetString() ?? string.Empty;
            }
            else
            {
                return Result.Fail($"'placeholderText' config is not valid");
            }
        }

        return Result.Ok();
    }

    private Result ApplyTextConfig(JsonElement config, DropDownTextBox dropDownTextBox)
    {
        if (config.TryGetProperty("text", out var configValue))
        {
            if (configValue.IsBindingConfig())
            {
                _textBinder = PropertyBinder<string>.Create(dropDownTextBox.InnerTextBox, this)
                .Binding(TextBox.TextProperty, BindingMode.TwoWay, nameof(Text))
                .Setter((value) =>
                {
                    // If auto trim is enabled then trim the text before setting the property
                    Text = _autoTrim ? value.Trim() : value;
                })
                .Getter(() =>
                {
                    return Text;
                });

                return _textBinder.Initialize(configValue);
            }
            else
            {
                return Result.Fail($"'text' config is not valid");
            }
        }

        return Result.Ok();
    }

    private Result ApplyButtonIconConfig(JsonElement config, StackPanel buttonPanel)
    {
        if (config.TryGetProperty("buttonIcon", out var textProperty))
        {
            // Check the type
            if (textProperty.ValueKind != JsonValueKind.String)
            {
                return Result<bool>.Fail("'buttonIcon' property must be a string");
            }

            // Apply the property
            var buttonIcon = textProperty.GetString();

            if (!string.IsNullOrEmpty(buttonIcon))
            {
                string glyph = string.Empty;
                if (Enum.TryParse(buttonIcon, out Symbol symbol))
                {
                    // String is a valid Symbol enum value
                    glyph = ((char)symbol).ToString();
                }
                else
                {
                    // Try the string as a unicode character
                    glyph = buttonIcon;
                }

                var fontIcon = new FontIcon()
                    .Glyph(glyph);

                buttonPanel.Children.Add(fontIcon);
            }
        }

        return Result.Ok();
    }

    private Result ApplyButtonTextConfig(JsonElement config, StackPanel buttonPanel)
    {
        if (config.TryGetProperty("buttonText", out var textProperty))
        {
            // Check the type
            if (textProperty.ValueKind != JsonValueKind.String)
            {
                return Result<bool>.Fail("'buttonText' property must be a string");
            }

            var textBlock = new TextBlock();
            if (buttonPanel.Children.Count > 0)
            {
                // Add a gap between the icon and the text
                textBlock.Margin = new Thickness(8, 0, 0, 0);
            }

            if (textProperty.IsBindingConfig())
            {
                _buttonTextBinder = PropertyBinder<string>.Create(textBlock, this)
                    .Binding(TextBlock.TextProperty, BindingMode.OneWay, nameof(ButtonText))
                    .Setter((value) =>
                    {
                        ButtonText = value;
                    });

                var initResult = _buttonTextBinder.Initialize(textProperty);
                if (initResult.IsFailure)
                {
                    return Result.Fail("Failed to initialize button text binding")
                        .WithErrors(initResult);
                }

                buttonPanel.Children.Add(textBlock);
            }
            else
            {
                // Apply the property
                var buttonText = textProperty.GetString();

                if (!string.IsNullOrEmpty(buttonText))
                {
                    textBlock.Text = buttonText;
                    buttonPanel.Children.Add(textBlock);
                }
            }
        }

        return Result.Ok();
    }

    private Result ApplyValuesConfig(JsonElement config, DropDownTextBox dropDownTextBox)
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
                    _valuesBinder = PropertyBinder<List<string>>.Create(dropDownTextBox.InnerListView, this)
                        .Binding(ListView.ItemsSourceProperty, BindingMode.TwoWay, nameof(Values))
                        .Getter(() => Values)
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

    private Result ApplyAutoTrimConfig(JsonElement config, DropDownTextBox dropDownTextBox)
    {
        if (config.TryGetProperty("autoTrim", out var autoTrimValue))
        {
            // Check the type
            if (autoTrimValue.ValueKind != JsonValueKind.True &&
                autoTrimValue.ValueKind != JsonValueKind.False)
            {
                return Result.Fail("'autoTrim' property must be a boolean");
            }

            // Apply the property
            var autoTrim = autoTrimValue.GetBoolean();
            if (!autoTrim)
            {
                // Auto trim is on by default
                _autoTrim = false;
            }
        }

        if (_autoTrim)
        {
            dropDownTextBox.InnerTextBox.LostFocus += (s, e) =>
            {
                // If auto trim is enabled then trim the text displayed in the TextBox
                dropDownTextBox.InnerTextBox.Text = dropDownTextBox.InnerTextBox.Text.Trim();
            };
        }

        return Result.Ok();
    }

    protected override void OnFormDataChanged(string propertyPath)
    {
        _isEnabledBinder?.OnFormDataChanged(propertyPath);
        _textBinder?.OnFormDataChanged(propertyPath);
        _placeholderTextBinder?.OnFormDataChanged(propertyPath);
        _buttonTextBinder?.OnFormDataChanged(propertyPath);
        _valuesBinder?.OnFormDataChanged(propertyPath);
    }

    protected override void OnMemberDataChanged(string propertyName)
    {
        _isEnabledBinder?.OnFormDataChanged(propertyName);  
        _textBinder?.OnMemberDataChanged(propertyName);
        _placeholderTextBinder?.OnFormDataChanged(propertyName);
        _buttonTextBinder?.OnFormDataChanged(propertyName);
        _valuesBinder?.OnFormDataChanged(propertyName);
    }

    protected override void OnElementUnloaded()
    {
        _textBinder?.OnElementUnloaded();
        _isEnabledBinder?.OnElementUnloaded();
        _placeholderTextBinder?.OnElementUnloaded();
        _buttonTextBinder?.OnElementUnloaded();
        _valuesBinder?.OnElementUnloaded();
    }
}
