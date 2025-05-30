using Celbridge.UserInterface.Services.Forms;
using System.Text.Json;
using Windows.System;

namespace Celbridge.UserInterface.ViewModels.Forms;

public partial class TextBoxElement : FormElement
{
    public static Result<FrameworkElement> CreateTextBox(JsonElement config, FormBuilder formBuilder)
    {
        var formElement = ServiceLocator.AcquireService<TextBoxElement>();
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

    private bool _autoTrim = true;

    protected override Result<FrameworkElement> CreateUIElement(JsonElement config, FormBuilder formBuilder)
    {
        //
        // Create the UI element
        //

        var textBox = new TextBox();
        textBox.DataContext = this;

        textBox.KeyDown += (sender, e) =>
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
            "checkSpelling",
            "isReadOnly",
            "autoTrim"
        });

        if (validateResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail("Invalid form element configuration")
                .WithErrors(validateResult);
        }

        //
        // Apply common element config properties
        //

        var commonConfigResult = ApplyCommonConfig(textBox, config);
        if (commonConfigResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply common config properties")
                .WithErrors(commonConfigResult);
        }

        //
        // Apply element-specific config properties
        //

        var isEnabledResult = ApplyIsEnabledConfig(config, textBox);
        if (isEnabledResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'isEnabled' config")
                .WithErrors(isEnabledResult);
        }

        // Todo: Set this via a config property
        textBox.TextWrapping = TextWrapping.Wrap;

        var headerResult = ApplyHeaderConfig(config, textBox);
        if (headerResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'header' config property")
                .WithErrors(headerResult);        
        }

        var placeholderResult = ApplyPlaceholderTextConfig(config, textBox);
        if (placeholderResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'placeholderText' config property")
                .WithErrors(placeholderResult);
        }

        var checkSpellingResult = ApplyCheckSpellingConfig(config, textBox);
        if (checkSpellingResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'checkSpelling' config property")
                .WithErrors(checkSpellingResult);
        }

        var textResult = ApplyTextConfig(config, textBox);
        if (textResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'text' config property")
                .WithErrors(textResult);
        }

        var readOnlyResult = ApplyReadOnlyConfig(config, textBox);
        if (readOnlyResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'isReadOnly' config property")
                .WithErrors(readOnlyResult);
        }

        var autoTrimResult = ApplyAutoTrimConfig(config, textBox);
        if (autoTrimResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'autoTrim' config property")
                .WithErrors(autoTrimResult);
        }

        return Result<FrameworkElement>.Ok(textBox);
    }

    private Result ApplyIsEnabledConfig(JsonElement config, TextBox textBox)
    {
        if (config.TryGetProperty("isEnabled", out var configValue))
        {
            if (configValue.IsBindingConfig())
            {
                _isEnabledBinder = PropertyBinder<bool>.Create(textBox, this)
                    .Binding(TextBox.IsEnabledProperty, BindingMode.OneWay, nameof(IsEnabled))
                    .Setter((value) =>
                    {
                        IsEnabled = value;
                    });

                return _isEnabledBinder.Initialize(configValue);
            }
            else if (configValue.ValueKind == JsonValueKind.True)
            {
                textBox.IsEnabled = false;
            }
            else if (configValue.ValueKind == JsonValueKind.False)
            {
                textBox.IsEnabled = false;
            }
            else
            {
                return Result<bool>.Fail("'isEnabled' config is not valid");
            }
        }

        return Result.Ok();
    }

    private Result ApplyHeaderConfig(JsonElement config, TextBox textBox)
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
            textBox.Header = header;
        }

        return Result.Ok();
    }

    private Result ApplyPlaceholderTextConfig(JsonElement config, TextBox textBox)
    {
        if (config.TryGetProperty("placeholderText", out var configValue))
        {
            if (configValue.IsBindingConfig())
            {
                _placeholderTextBinder = PropertyBinder<string>.Create(textBox, this)
                .Binding(TextBox.PlaceholderTextProperty, BindingMode.OneWay, nameof(PlaceholderText))
                .Setter((value) =>
                {
                    PlaceholderText = value;
                });

                return _placeholderTextBinder.Initialize(configValue);
            }
            else if (configValue.ValueKind == JsonValueKind.String)
            {
                textBox.PlaceholderText = configValue.GetString() ?? string.Empty;
            }
            else
            {
                return Result.Fail($"'placeholderText' config is not valid");
            }
        }

        return Result.Ok();
    }

    private Result ApplyCheckSpellingConfig(JsonElement config, TextBox textBox)
    {
        if (config.TryGetProperty("checkSpelling", out var jsonValue))
        {
            // Check the type
            if (jsonValue.ValueKind == JsonValueKind.True)
            {
                textBox.IsSpellCheckEnabled = true;
            }
            else if (jsonValue.ValueKind == JsonValueKind.False)
            {
                textBox.IsSpellCheckEnabled = false;
            }
            else
            {
                return Result.Fail($"'checkSpelling' config is not valid");
            }

            return Result.Ok();
        }

        return Result.Ok();
    }

    private Result ApplyTextConfig(JsonElement config, TextBox textBox)
    {
        if (config.TryGetProperty("text", out var configValue))
        {
            if (configValue.IsBindingConfig())
            {
                _textBinder = PropertyBinder<string>.Create(textBox, this)
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

    private Result ApplyReadOnlyConfig(JsonElement config, TextBox textBox)
    {
        if (config.TryGetProperty("isReadOnly", out var isReadOnlyValue))
        {
            // Check the type
            if (isReadOnlyValue.ValueKind != JsonValueKind.True &&
                isReadOnlyValue.ValueKind != JsonValueKind.False)
            {
                return Result.Fail("'isReadOnly' property must be a boolean");
            }

            // Apply the property
            var isReadOnly = isReadOnlyValue.GetBoolean();
            if (isReadOnly)
            {
                // Text can be selected but not edited
                textBox.IsReadOnly = true;
                textBox.Opacity = 0.6;
            }
        }

        return Result.Ok();
    }

    private Result ApplyAutoTrimConfig(JsonElement config, TextBox textBox)
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
            textBox.LostFocus += (s, e) =>
            {
                // If auto trim is enabled then trim the text displayed in the TextBox
                textBox.Text = textBox.Text.Trim();
            };
        }

        return Result.Ok();
    }

    protected override void OnFormDataChanged(string propertyPath)
    {
        _isEnabledBinder?.OnFormDataChanged(propertyPath);
        _textBinder?.OnFormDataChanged(propertyPath);
        _placeholderTextBinder?.OnFormDataChanged(propertyPath);    
    }

    protected override void OnMemberDataChanged(string propertyName)
    {
        _isEnabledBinder?.OnFormDataChanged(propertyName);  
        _textBinder?.OnMemberDataChanged(propertyName);
        _placeholderTextBinder?.OnFormDataChanged(propertyName);
    }

    protected override void OnElementUnloaded()
    {
        _textBinder?.OnElementUnloaded();
        _isEnabledBinder?.OnElementUnloaded();
        _placeholderTextBinder?.OnElementUnloaded();
    }
}
