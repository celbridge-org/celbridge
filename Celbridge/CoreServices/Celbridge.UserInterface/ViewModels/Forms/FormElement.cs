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

    public bool RequiresChangeNotifications { get; set; }

    private FrameworkElement? _frameworkElement;

    private PropertyBinder<string>? _visibilityBinder;
    private PropertyBinder<string>? _tooltipBinder;

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

        _frameworkElement = uiElement;

        if (RequiresChangeNotifications)
        {
            RegisterEvents(uiElement);
        }

        return Result<FrameworkElement>.Ok(uiElement);
    }

    protected abstract Result<FrameworkElement> CreateUIElement(JsonElement config, FormBuilder formBuilder);

    protected Result ValidateConfigKeys(JsonElement config, HashSet<string> validConfigKeys)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return Result.Fail("Form element config must be an object");
        }

        foreach (var property in config.EnumerateObject())
        {
            var configKey = property.Name;
            switch (configKey)
            {
                case "element":
                case "visibility":
                case "horizontalAlignment":
                case "verticalAlignment":
                case "tooltip":
                    // Skip general config properties that apply to all elements
                    continue;
            }

            if (!validConfigKeys.Contains(configKey))
            {
                return Result.Fail($"Invalid form element property: '{configKey}'");
            }
        }

        return Result.Ok();
    }

    protected Result ApplyCommonConfig(FrameworkElement frameworkElement, JsonElement config)
    {
        var visibilityResult = ApplyVisibilityConfig(frameworkElement, config);
        if (visibilityResult.IsFailure)
        {
            return Result.Fail($"Failed to apply 'visibility' config")
                .WithErrors(visibilityResult);
        }

        var alignmentResult = ApplyAlignmentConfig(frameworkElement, config);
        if (alignmentResult.IsFailure)
        {
            return Result.Fail($"Failed to apply 'alignment' config")
                .WithErrors(alignmentResult);
        }

        var tooltipResult = ApplyTooltipConfig(frameworkElement, config);
        if (alignmentResult.IsFailure)
        {
            return Result.Fail($"Failed to apply 'tooltip' config")
                .WithErrors(alignmentResult);
        }

        return Result.Ok();
    }

    private Result ApplyVisibilityConfig(FrameworkElement frameworkElement, JsonElement config)
    {
        if (config.TryGetProperty("visibility", out var configValue))
        {
            if (PropertyBinder<string>.IsBindingConfig(configValue))
            {
                _visibilityBinder = PropertyBinder<string>.Create(frameworkElement, this)
                    .Setter((value) =>
                    {
                        if (Enum.TryParse<Visibility>(value, out var visibility))
                        {
                            frameworkElement.Visibility = visibility;
                        }
                    });

                return _visibilityBinder.Initialize(configValue);
            }
            else if (configValue.ValueKind == JsonValueKind.String)
            {
                var jsonValue = configValue.GetString() ?? string.Empty;
                if (Enum.TryParse<Visibility>(jsonValue, out var visibility))
                {
                    frameworkElement.Visibility = visibility;
                }
            }
            else
            {
                return Result.Fail("'visibility' config is not valid");
            }
        }

        return Result.Ok();
    }

    private Result ApplyAlignmentConfig(FrameworkElement frameworkElement, JsonElement config)
    {
        if (config.TryGetProperty("horizontalAlignment", out var horizontalProperty))
        {
            if (horizontalProperty.ValueKind != JsonValueKind.String)
            {
                return Result.Fail("'horizontalAlignment' property must be a string");
            }

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
            if (verticalAlignmentProperty.ValueKind != JsonValueKind.String)
            {
                return Result.Fail("'verticalAlignment' property must be a string");
            }

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
        if (config.TryGetProperty("tooltip", out var configValue))
        {
            if (PropertyBinder<string>.IsBindingConfig(configValue))
            {
                _tooltipBinder = PropertyBinder<string>.Create(frameworkElement, this)
                    .Setter((value) =>
                    {
                        if (string.IsNullOrEmpty(value))
                        {
                            ToolTipService.SetToolTip(frameworkElement, null);
                        }
                        else
                        {
                            ToolTipService.SetToolTip(frameworkElement, value);
                        }
                    });

                return _tooltipBinder.Initialize(configValue);
            }
            else if (configValue.ValueKind == JsonValueKind.String)
            {
                var value = configValue.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(value))
                {
                    ToolTipService.SetToolTip(frameworkElement, value);
                }
            }
            else
            {
                return Result.Fail("'tooltip' config is not valid");
            }
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

        // Tooltip binder only needs to respond to form property changes
        _tooltipBinder?.OnFormDataChanged(propertyPath);

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
