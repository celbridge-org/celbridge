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

    private PropertyBinder<bool>? _isEnabledBinder;

    [ObservableProperty]
    private string _text = string.Empty;
    private PropertyBinder<string>? _textBinder;

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
            "placeholder",
            "checkSpelling",
            "isReadOnly"
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

        var placeholderResult = ApplyPlaceholderConfig(config, textBox);
        if (placeholderResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'placeholder' config property")
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

        return Result<FrameworkElement>.Ok(textBox);
    }

    private Result ApplyIsEnabledConfig(JsonElement config, TextBox textBox)
    {
        if (config.TryGetProperty("isEnabled", out var configValue))
        {
            if (configValue.IsBindingConfig())
            {
                _isEnabledBinder = PropertyBinder<bool>.Create(textBox, this)
                    .Setter((value) =>
                    {
                        textBox.IsEnabled = value;
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

    private Result ApplyPlaceholderConfig(JsonElement config, TextBox textBox)
    {
        if (config.TryGetProperty("placeholder", out var jsonValue))
        {
            // Check the type
            if (jsonValue.ValueKind != JsonValueKind.String)
            {
                return Result.Fail("'placeholder' property must be a string");
            }

            // Todo: Support binding

            // Apply the property
            var placeholder = jsonValue.GetString();
            textBox.PlaceholderText = placeholder;
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
                    Text = value;
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
            if (isReadOnlyValue.GetBoolean())
            {
                // Text can be selected but not edited
                textBox.IsReadOnly = true;
                textBox.Opacity = 0.6;
            }
        }

        return Result.Ok();
    }

    protected override void OnFormDataChanged(string propertyPath)
    {
        _isEnabledBinder?.OnFormDataChanged(propertyPath);
        _textBinder?.OnFormDataChanged(propertyPath);
    }

    protected override void OnMemberDataChanged(string propertyName)
    {
        _textBinder?.OnMemberDataChanged(propertyName);
    }

    protected override void OnElementUnloaded()
    {
        _textBinder?.OnElementUnloaded();
        _isEnabledBinder?.OnElementUnloaded();
    }
}
