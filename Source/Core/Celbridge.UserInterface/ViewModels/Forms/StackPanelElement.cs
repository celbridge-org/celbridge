using Celbridge.UserInterface.Services.Forms;
using System.Text.Json;

namespace Celbridge.UserInterface.ViewModels.Forms;

public class StackPanelElement : FormElement
{
    private const int DefaultStackPanelSpacing = 8;
    private const Orientation DefaultOrientation = Orientation.Vertical;

    public static Result<FrameworkElement> CreateStackPanel(JsonElement config, FormBuilder formBuilder)
    {
        var formElement = ServiceLocator.AcquireService<StackPanelElement>();
        return formElement.Create(config, formBuilder);
    }

    protected override Result<FrameworkElement> CreateUIElement(JsonElement config, FormBuilder formBuilder)
    {
        //
        // Create the UI element
        //

        var stackPanel = new StackPanel();
        stackPanel.DataContext = this;

        //
        // Check all specified config properties are supported
        //

        var validateResult = ValidateConfigKeys(config, new HashSet<string>()
        {
            "spacing",
            "orientation",
            "children"
        });

        if (validateResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail("Invalid form element configuration")
                .WithErrors(validateResult);
        }

        var commonConfigResult = ApplyCommonConfig(stackPanel, config);
        if (commonConfigResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply common configuration properties to StackPanel")
                .WithErrors(commonConfigResult);
        }

        //
        // Apply element-specific config properties
        //

        var spacingResult = ApplySpacingConfig(config, stackPanel);
        if (spacingResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'spacing' config property")
                .WithErrors(spacingResult);
        }

        var orientationResult = ApplyOrientationConfig(config, stackPanel);
        if (orientationResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'orientation' config property")
                .WithErrors(orientationResult);
        }

        var childrenResult = ApplyChildrenConfig(config, stackPanel, formBuilder);
        if (childrenResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'children' config property")
                .WithErrors(childrenResult);
        }

        return Result<FrameworkElement>.Ok(stackPanel);
    }

    private Result ApplySpacingConfig(JsonElement config, StackPanel stackPanel)
    {
        if (config.TryGetProperty("spacing", out var spacingValue))
        {
            // Check the type
            if (spacingValue.ValueKind != JsonValueKind.Number)
            {
                return Result.Fail("'spacing' property must be a number");
            }

            // Apply the property
            var spacing = spacingValue.GetInt32();
            stackPanel.Spacing = spacing;
        }
        else
        {
            stackPanel.Spacing = DefaultStackPanelSpacing;
        }

        return Result.Ok();
    }

    private Result ApplyOrientationConfig(JsonElement config, StackPanel stackPanel)
    {
        if (config.TryGetProperty("orientation", out var orientationValue))
        {
            // Check the type
            if (orientationValue.ValueKind != JsonValueKind.String)
            {
                return Result.Fail("'orientation' property must be a string");
            }

            // Apply the property
            var orientation = orientationValue.GetString();
            if (orientation == "Horizontal")
            {
                stackPanel.Orientation = Orientation.Horizontal;
            }
            else if (orientation == "Vertical")
            {
                stackPanel.Orientation = Orientation.Vertical;
            }
            else
            {
                return Result<UIElement>.Fail($"Invalid orientation value: '{orientation}'");
            }
        }
        else
        {
            stackPanel.Orientation = DefaultOrientation;
        }

        return Result.Ok();
    }

    private Result ApplyChildrenConfig(JsonElement config, StackPanel stackPanel, FormBuilder formBuilder)
    {
        // Add child controls
        if (config.TryGetProperty("children", out var childrenValue))
        {
            // Check the type
            if (childrenValue.ValueKind != JsonValueKind.Array)
            {
                return Result.Fail("'children' property must be an array");
            }

            // Apply the property
            foreach (var child in childrenValue.EnumerateArray())
            {
                var childControl = formBuilder.CreateFormElement(child);
                if (childControl is null)
                {
                    return Result<UIElement>.Fail("Failed to create child control");
                }

                stackPanel.Children.Add(childControl);
            }
        }

        return Result.Ok();
    }
}
