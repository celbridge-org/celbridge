using Celbridge.UserInterface.Services.Forms;
using System.Text.Json;

namespace Celbridge.UserInterface.ViewModels.Forms;

public partial class ButtonElement : FormElement
{
    public static Result<FrameworkElement> CreateButton(JsonElement config, FormBuilder formBuilder)
    {
        var formElement = ServiceLocator.AcquireService<ButtonElement>();
        return formElement.Create(config, formBuilder);
    }

    [ObservableProperty]
    private bool _isEnabled = true;
    private PropertyBinder<bool>? _isEnabledBinder;

    [ObservableProperty]
    private string _buttonText = string.Empty;
    private PropertyBinder<string>? _buttonTextBinder;

    protected override Result<FrameworkElement> CreateUIElement(JsonElement config, FormBuilder formBuilder)
    {
        //
        // Create the UI element
        //

        var button = new Button();
        button.DataContext = this;

        // Add a horizontal panel for the button content
        var buttonPanel = new StackPanel();
        buttonPanel.Orientation = Orientation.Horizontal;
        button.Content = buttonPanel;

        //
        // Check all specified config properties are supported
        //

        var validateResult = ValidateConfigKeys(config, new HashSet<string>()
        {
            "isEnabled",
            "icon",
            "text",
            "buttonId"
        });

        if (validateResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail("Invalid Button configuration")
                .WithErrors(validateResult);
        }

        //
        // Apply common config properties
        //

        var commonConfigResult = ApplyCommonConfig(button, config);
        if (commonConfigResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply common config properties")
                .WithErrors(commonConfigResult);
        }

        //
        // Apply element-specific config properties
        //

        var isEnabledResult = ApplyIsEnabledConfig(config, button);
        if (isEnabledResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'isEnabled' config")
                .WithErrors(isEnabledResult);
        }

        var iconResult = ApplyIconConfig(config, buttonPanel);
        if (iconResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'icon' config property")
                .WithErrors(iconResult);
        }

        var textResult = ApplyTextConfig(config, buttonPanel);
        if (textResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'text' config property")
                .WithErrors(textResult);
        }

        var buttonIdResult = ApplyButtonIdConfig(config, button);
        if (buttonIdResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'buttonId' config property")
                .WithErrors(buttonIdResult);
        }

        return Result<FrameworkElement>.Ok(button);
    }

    private Result ApplyIsEnabledConfig(JsonElement config, Button button)
    {
        if (config.TryGetProperty("isEnabled", out var configValue))
        {
            if (configValue.IsBindingConfig())
            {
                _isEnabledBinder = PropertyBinder<bool>.Create(button, this)
                    .Binding(Button.IsEnabledProperty, BindingMode.OneWay, nameof(IsEnabled))
                    .Setter((value) =>
                    {
                        IsEnabled = value;
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

    private Result ApplyIconConfig(JsonElement config, StackPanel buttonPanel)
    {
        if (config.TryGetProperty("icon", out var textProperty))
        {
            // Check the type
            if (textProperty.ValueKind != JsonValueKind.String)
            {
                return Result<bool>.Fail("'icon' property must be a string");
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

    private Result ApplyTextConfig(JsonElement config, StackPanel buttonPanel)
    {
        if (config.TryGetProperty("text", out var textProperty))
        {
            // Check the type
            if (textProperty.ValueKind != JsonValueKind.String)
            {
                return Result<bool>.Fail("'text' property must be a string");
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

    private Result ApplyButtonIdConfig(JsonElement config, Button button)
    {
        if (config.TryGetProperty("buttonId", out var textProperty))
        {
            // Check the type
            if (textProperty.ValueKind != JsonValueKind.String)
            {
                return Result<bool>.Fail("'text' property must be a string");
            }

            // Apply the property
            var buttonId = textProperty.GetString();

            if (string.IsNullOrEmpty(buttonId))
            {
                return Result<bool>.Fail("'buttonId' property must not be empty");
            }

            button.Click += (sender, args) =>
            {
                // Handle the button click
                OnButtonClicked(buttonId);
            };
        }

        return Result.Ok();
    }

    protected override void OnFormDataChanged(string propertyPath)
    {
        _isEnabledBinder?.OnFormDataChanged(propertyPath);
        _buttonTextBinder?.OnFormDataChanged(propertyPath);
    }

    protected override void OnMemberDataChanged(string propertyName)
    {
        _isEnabledBinder?.OnMemberDataChanged(propertyName);
        _buttonTextBinder?.OnMemberDataChanged(propertyName);
    }

    protected override void OnElementUnloaded()
    {
        _isEnabledBinder?.OnElementUnloaded();
        _buttonTextBinder?.OnElementUnloaded();
    }

    private void OnButtonClicked(string buttonId)
    {
        FormDataProvider?.OnButtonClicked(buttonId);
    }
}
