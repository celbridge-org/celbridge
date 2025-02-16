using Celbridge.UserInterface.Services.Forms;
using Microsoft.UI.Text;
using System.Text.Json;
using Windows.UI.Text;

namespace Celbridge.UserInterface.ViewModels.Forms;

public partial class TextBlockElement : FormElement
{
    public static Result<FrameworkElement> CreateTextBlock(JsonElement config, FormBuilder formBuilder)
    {
        var formElement = ServiceLocator.AcquireService<TextBlockElement>();
        return formElement.Create(config, formBuilder);
    }

    [ObservableProperty]
    private string _text = string.Empty;
    private PropertyBinder<string>? _textBinder;

    protected override Result<FrameworkElement> CreateUIElement(JsonElement config, FormBuilder formBuilder)
    {
        //
        // Create the UI element
        //

        var textBlock = new TextBlock();
        textBlock.DataContext = this;

        //
        // Check all specified config properties are supported
        //

        var validateResult = ValidateConfigKeys(config, new HashSet<string>()
        {
            "text",
            "italic",
            "bold"
        });

        if (validateResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail("Invalid form element configuration")
                .WithErrors(validateResult);
        }

        //
        // Apply common element config properties
        //

        var commonConfigResult = ApplyCommonConfig(textBlock, config);
        if (commonConfigResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply common config properties")
                .WithErrors(commonConfigResult);
        }

        //
        // Apply element-specific config properties
        //

        var italicResult = ApplyItalicConfig(config, textBlock);
        if (italicResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'italic' config property")
                .WithErrors(italicResult);
        }

        var boldResult = ApplyBoldConfig(config, textBlock);
        if (boldResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'bold' config property")
                .WithErrors(boldResult);
        }

        var textResult = ApplyTextConfig(config, textBlock);
        if (textResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'text' config property")
                .WithErrors(textResult);
        }

        return Result<FrameworkElement>.Ok(textBlock);
    }

    private Result ApplyItalicConfig(JsonElement config, TextBlock textBlock)
    {
        if (config.TryGetProperty("italic", out var italicValue))
        {
            // Check the type
            if (italicValue.ValueKind != JsonValueKind.True &&
                italicValue.ValueKind != JsonValueKind.False)
            {
                return Result.Fail("'italic' property must be a boolean");
            }

            // Apply the property
            var italic = italicValue.GetBoolean();
            if (italic)
            {
                textBlock.FontStyle = FontStyle.Italic;
            }
        }

        return Result.Ok();
    }

    private Result ApplyBoldConfig(JsonElement config, TextBlock textBlock)
    {
        if (config.TryGetProperty("bold", out var boldValue))
        {
            // Check the type
            if (boldValue.ValueKind != JsonValueKind.True &&
                boldValue.ValueKind != JsonValueKind.False)
            {
                return Result.Fail("'bold' property must be a boolean");
            }

            // Apply the property
            var bold = boldValue.GetBoolean();
            if (boldValue.GetBoolean())
            {
                textBlock.FontWeight = FontWeights.Bold;
            }
        }

        return Result.Ok();
    }

    private Result ApplyTextConfig(JsonElement config, TextBlock textBlock)
    {
        if (config.TryGetProperty("text", out var configValue))
        {
            if (PropertyBinder<string>.IsBindingConfig(configValue))
            {
                _textBinder = PropertyBinder<string>.Create(textBlock, this)
                .Binding(TextBlock.TextProperty, BindingMode.OneWay, nameof(Text))
                .Setter((value) =>
                {
                    Text = value;
                });

                return _textBinder.Initialize(configValue);
            }
            else if (configValue.ValueKind == JsonValueKind.String)
            {
                textBlock.Text = configValue.GetString() ?? string.Empty;
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
        Guard.IsNotNull(_textBinder);

        _textBinder.OnFormDataChanged(propertyPath);
    }

    protected override void OnMemberDataChanged(string propertyName)
    {
        Guard.IsNotNull(_textBinder);

        _textBinder.OnMemberDataChanged(propertyName);
    }
}
