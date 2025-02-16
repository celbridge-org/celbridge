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
            "checkSpelling"
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

        return Result<FrameworkElement>.Ok(textBox);
    }

    private Result ApplyIsEnabledConfig(JsonElement config, TextBox textBox)
    {
        if (config.TryGetProperty("isEnabled", out var configValue))
        {
            if (PropertyBinder<bool>.IsBindingConfig(configValue))
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
            var header = jsonValue.GetString();
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
            if (jsonValue.ValueKind != JsonValueKind.String)
            {
                return Result.Fail("'checkSpelling' property must be a string");
            }

            // Apply the property
            var jsonText = jsonValue.GetString();
            if (!bool.TryParse(jsonText, out var checkSpelling))
            {
                return Result.Fail("Failed to parse 'checkSpelling' property as a boolean");
            }

            textBox.IsSpellCheckEnabled = checkSpelling;

            return Result.Ok();
        }

        return Result.Ok();
    }

    private Result ApplyTextConfig(JsonElement config, TextBox textBox)
    {
        if (config.TryGetProperty("text", out var configValue))
        {
            if (PropertyBinder<string>.IsBindingConfig(configValue))
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

    protected override void OnFormDataChanged(string propertyPath)
    {
        _isEnabledBinder?.OnFormDataChanged(propertyPath);
        _textBinder?.OnFormDataChanged(propertyPath);
    }

    protected override void OnMemberDataChanged(string propertyName)
    {
        _textBinder?.OnMemberDataChanged(propertyName);
    }
}
