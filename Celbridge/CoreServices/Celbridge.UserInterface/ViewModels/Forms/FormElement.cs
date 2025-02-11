using System.ComponentModel;
using System.Text.Json;
using Celbridge.Forms;
using Celbridge.UserInterface.Services.Forms;

namespace Celbridge.UserInterface.ViewModels.Forms;

/// <summary>
/// Base class for element view models that bind to a form data provider.
/// </summary>
public abstract partial class FormElement : ObservableObject
{
    private IFormDataProvider? _formDataProvider;
    public IFormDataProvider FormDataProvider => _formDataProvider!;

    public bool HasBindings { get; set; }

    public Result<FrameworkElement> Create(JsonElement config, FormBuilder formBuilder)
    {
        Guard.IsNull(_formDataProvider);
        _formDataProvider = formBuilder.FormDataProvider;

        var createUIResult = CreateUIElement(config, formBuilder);
        if (createUIResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail("Failed to create UI element")
                .WithErrors(createUIResult);
        }
        var uiElement = createUIResult.Value;

        if (HasBindings)
        {
            RegisterEvents(uiElement);
        }

        return Result<FrameworkElement>.Ok(uiElement);
    }

    protected abstract Result<FrameworkElement> CreateUIElement(JsonElement config, FormBuilder formBuilder);

    protected Result ValidateConfigKeys(JsonElement config, HashSet<string> validConfigKeys)
    {
        var keys = new List<string>();
        if (config.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in config.EnumerateObject())
            {
                var configKey = property.Name;
                if (configKey == "element" ||
                    configKey == "horizontalAlignment" ||
                    configKey == "verticalAlignment" ||
                    configKey == "tooltip")
                {
                    // Skip general config properties that apply to all elements
                    continue;
                }

                if (!validConfigKeys.Contains(configKey))
                {
                    return Result.Fail($"Invalid form element property: '{configKey}'");
                }
            }
        }

        return Result.Ok();
    }

    protected Result ApplyCommonConfig(FrameworkElement frameworkElement, JsonElement config)
    {
        var alignmentResult = ApplyAlignmentConfig(frameworkElement, config);
        if (alignmentResult.IsFailure)
        {
            return Result.Fail($"Failed to apply alignment config")
                .WithErrors(alignmentResult);
        }

        var tooltipResult = ApplyTooltipConfig(frameworkElement, config);
        if (alignmentResult.IsFailure)
        {
            return Result.Fail($"Failed to apply tooltip config")
                .WithErrors(alignmentResult);
        }

        return Result.Ok();
    }

    private Result ApplyAlignmentConfig(FrameworkElement frameworkElement, JsonElement config)
    {
        if (config.TryGetProperty("horizontalAlignment", out var horizontalProperty))
        {
            var horizontalAlignment = horizontalProperty.GetString();
            switch (horizontalAlignment)
            {
                case "Left":
                    frameworkElement.HorizontalAlignment = HorizontalAlignment.Left;
                    break;

                case "Center":
                    frameworkElement.HorizontalAlignment = HorizontalAlignment.Center;
                    break;

                case "Right":
                    frameworkElement.HorizontalAlignment = HorizontalAlignment.Right;
                    break;

                case "Stretch":
                    frameworkElement.HorizontalAlignment = HorizontalAlignment.Stretch;
                    break;

                default:
                    return Result.Fail($"Invalid horizontal alignment value: '{horizontalAlignment}'");
            }
        }

        if (config.TryGetProperty("verticalAlignment", out var verticalAlignmentProperty))
        {
            var verticalAlignment = verticalAlignmentProperty.GetString();
            switch (verticalAlignment)
            {
                case "Top":
                    frameworkElement.VerticalAlignment = VerticalAlignment.Top;
                    break;
                case "Center":
                    frameworkElement.VerticalAlignment = VerticalAlignment.Center;
                    break;
                case "Bottom":
                    frameworkElement.VerticalAlignment = VerticalAlignment.Bottom;
                    break;
                case "Stretch":
                    frameworkElement.VerticalAlignment = VerticalAlignment.Stretch;
                    break;
                default:
                    return Result.Fail($"Invalid vertical alignment value: '{verticalAlignment}'");
            }
        }

        return Result.Ok();
    }

    private Result ApplyTooltipConfig(FrameworkElement frameworkElement, JsonElement config)
    {
        if (config.TryGetProperty("tooltip", out var tooltipText))
        {
            ToolTipService.SetToolTip(frameworkElement, tooltipText);
        }

        return Result.Ok();
    }

    protected void RegisterEvents(FrameworkElement frameworkElement)
    {
        // Listen for changes to form data and member data
        FormDataProvider.FormPropertyChanged += OnFormPropertyChanged;
        PropertyChanged += OnPropertyChanged;
        frameworkElement.Unloaded += FrameworkElement_Unloaded;        
    }

    private void FrameworkElement_Unloaded(object sender, RoutedEventArgs e)
    {
        var frameworkElement = sender as FrameworkElement;
        Guard.IsNotNull(frameworkElement);
        UnregisterEvents(frameworkElement);
    }

    private void UnregisterEvents(FrameworkElement frameworkElement)
    {
        // Unregister event listeners
        FormDataProvider.FormPropertyChanged -= OnFormPropertyChanged;
        PropertyChanged -= OnPropertyChanged;
        frameworkElement.Unloaded -= FrameworkElement_Unloaded;

        _formDataProvider = null;
    }

    private void OnFormPropertyChanged(string propertyPath)
    {
        Guard.IsNotNullOrEmpty(propertyPath);

        // Forward the event to the derived class
        OnFormDataChanged(propertyPath);
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Guard.IsNotNullOrEmpty(e.PropertyName);

        // Stop listening for form data changes while we update the form data
        FormDataProvider.FormPropertyChanged -= OnFormDataChanged;

        // Forward the event to the derived class
        var propertyName = e.PropertyName;
        OnMemberDataChanged(propertyName);

        // Resume listening for form data changes
        FormDataProvider.FormPropertyChanged += OnFormDataChanged;
    }

    protected virtual void OnFormDataChanged(string propertyPath)
    {}

    protected virtual void OnMemberDataChanged(string propertyName)
    {}
}
