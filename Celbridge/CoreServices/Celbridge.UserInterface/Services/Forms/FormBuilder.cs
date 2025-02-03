using Celbridge.Forms;
using Celbridge.Logging;
using Celbridge.UserInterface.ViewModels.Forms;
using Microsoft.UI.Text;
using System.Text.Json;
using Windows.System;
using Windows.UI.Text;

namespace Celbridge.UserInterface.Services.Forms;

public class FormBuilder
{
    private const int DefaultStackPanelSpacing = 8;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FormBuilder> _logger;

    private IFormDataProvider? _formDataProvider;

    private List<string> _buildErrors = new();

    public FormBuilder(
        IServiceProvider serviceProvider,
        ILogger<FormBuilder> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Result<object> BuildForm(string formName, JsonElement formConfig, IFormDataProvider formDataProvider)
    {
        _formDataProvider = formDataProvider;
        _buildErrors.Clear();

        var formPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            DataContext = formDataProvider,
            Spacing = DefaultStackPanelSpacing
        };

        try
        {
            foreach (var jsonElement in formConfig.EnumerateArray())
            {
                var uiElement = CreateUIElementFromJsonElement(jsonElement);
                if (uiElement is null)
                {
                    // The build has failed, but continue to report all the errors we can find
                    continue;
                }

                formPanel.Children.Add(uiElement);
            }

        }
        catch (Exception ex)
        {
            _buildErrors.Add($"An exception occurred when building form: {formName}. {ex}");
        }

        // Build has completed.
        // If any errors were encountered then fail the build, otherwise return the new root panel.

        if (_buildErrors.Count > 0)
        {
            // Log all build errors encountered (in reverse order)
            _logger.LogError($"Failed to build form: '{formName}'");
            _buildErrors.Reverse();
            foreach (var error in _buildErrors)
            {
                _logger.LogError(error);
            }
            _buildErrors.Clear();
            _formDataProvider = null;

            // Fail the build
            return Result<object>.Fail($"Failed to build form: {formName}");
        }
        _formDataProvider = null;

        formPanel.Loaded += (s, e) =>
        {
            var formDataProvider = formPanel.DataContext as IFormDataProvider;
            if (formDataProvider is not null)
            {
                formDataProvider.OnFormLoaded();
            }
        };

        formPanel.Unloaded += (s, e) =>
        {
            var formDataProvider = formPanel.DataContext as IFormDataProvider;
            if (formDataProvider is not null)
            {
                formDataProvider.OnFormUnloaded();
            }
        };

        return Result<object>.Ok(formPanel);
    }

    private UIElement? CreateUIElementFromJsonElement(JsonElement jsonElement)
    {
        if (jsonElement.ValueKind != JsonValueKind.Object)
        {
            _buildErrors.Add("Form array element is not an object");
            return null;
        }

        if (!jsonElement.TryGetProperty("element", out var element))
        {
            _buildErrors.Add("Form object does not contain an 'elementType' property");
            return null;
        }
        var elementName = element.GetString();

        UIElement? uiElement = null;        
        switch (elementName)
        {
            case "StackPanel":
                uiElement = CreateStackPanel(jsonElement);
                break;

            case "TextBox":
                uiElement = CreateTextBox(jsonElement);
                break;

            case "TextBlock":
                uiElement = CreateTextBlock(jsonElement);
                break;

            case "Button":
                uiElement = CreateButton(jsonElement);
                break;
        }

        if (uiElement is null)
        {
            _buildErrors.Add($"Failed to create element of type: '{elementName}'");
            return null;
        }

        return uiElement;
    }

    private StackPanel? CreateStackPanel(JsonElement jsonElement)
    {
        var stackPanel = new StackPanel();

        // Set the spacing between elements
        if (jsonElement.TryGetProperty("spacing", out var spacing))
        {
            stackPanel.Spacing = spacing.GetInt32();
        }
        else
        {
            stackPanel.Spacing = DefaultStackPanelSpacing;
        }

        // Set the orientation
        if (jsonElement.TryGetProperty("orientation", out var orientation))
        {
            var orientationString = orientation.GetString();
            if (orientationString == "Horizontal")
            {
                stackPanel.Orientation = Orientation.Horizontal;
            }
            else if (orientationString == "Vertical")
            {
                stackPanel.Orientation = Orientation.Vertical;
            }
            else
            {
                // Log the error and default to vertical
                _buildErrors.Add($"Invalid orientation value: '{orientationString}'");
            }
        }

        // Add child controls
        if (jsonElement.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                var childControl = CreateUIElementFromJsonElement(child);
                if (childControl is null)
                {
                    _buildErrors.Add("Failed to create child control");
                    continue;
                }
                
                stackPanel.Children.Add(childControl);
            }
        }

        return stackPanel;
    }

    private TextBox? CreateTextBox(JsonElement jsonElement)
    {
        var textBox = new TextBox();

        textBox.TextWrapping = TextWrapping.Wrap;

        if (!ApplyAlignmentConfig(textBox, jsonElement))
        {
            _buildErrors.Add($"Failed to apply alignment configuration to TextBox");
            return null;
        }

        // Check for unsupported config properties

        var validConfigKeys = new HashSet<string>() 
        { 
            "textBinding", 
            "header", 
            "placeholder", 
            "checkSpelling" 
        };
        if (!ValidateConfigKeys(jsonElement, validConfigKeys))
        {
            _buildErrors.Add("Invalid TextBox configuration");
            return null;
        }

        // Apply unbound properties

        if (jsonElement.TryGetProperty("header", out var header))
        {
            // Todo: Support localization
            textBox.Header = header.GetString();
        }

        if (jsonElement.TryGetProperty("placeholder", out var placeholder))
        {
            textBox.PlaceholderText = placeholder.GetString();
        }

        if (jsonElement.TryGetProperty("checkSpelling", out var checkSpelling))
        {
            textBox.IsSpellCheckEnabled = checkSpelling.GetBoolean();
        }

        // Apply property bindings

        if (GetBindingPropertyPath(jsonElement, "textBinding", out var propertyPath))
        {
            ApplyBinding<StringPropertyViewModel>(textBox, TextBox.TextProperty, BindingMode.TwoWay, propertyPath);
        }

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

        return textBox;
    }

    private TextBlock? CreateTextBlock(JsonElement jsonElement)
    {
        var textBlock = new TextBlock();

        if (!ApplyAlignmentConfig(textBlock, jsonElement))
        {
            _buildErrors.Add($"Failed to apply alignment configuration to TextBox");
            return null;
        }

        // Check all specified properties are supported

        var validConfigKeys = new HashSet<string>()
        {
            "textBinding",
            "text",
            "italic",
            "bold"
        };
        if (!ValidateConfigKeys(jsonElement, validConfigKeys))
        {
            _buildErrors.Add("Invalid TextBlock configuration");
            return null;
        }

        // Apply property bindings

        if (jsonElement.TryGetProperty("text", out var text))
        {
            // Todo: Support localization
            textBlock.Text = text.GetString();
        }

        if (jsonElement.TryGetProperty("italic", out var italic))
        {
            if (italic.GetBoolean())
            {
                textBlock.FontStyle = FontStyle.Italic;
            }
        }

        if (jsonElement.TryGetProperty("bold", out var bold))
        {
            if (bold.GetBoolean())
            {
                textBlock.FontWeight = FontWeights.Bold;
            }
        }

        // Apply bound properties

        if (GetBindingPropertyPath(jsonElement, "textBinding", out var propertyPath))
        {
            ApplyBinding<StringPropertyViewModel>(textBlock, TextBlock.TextProperty, BindingMode.OneWay, propertyPath);
        }

        return textBlock;
    }

    private Button? CreateButton(JsonElement jsonElement)
    {
        var button = new Button();

        if (!ApplyAlignmentConfig(button, jsonElement))
        {
            _buildErrors.Add($"Failed to apply alignment configuration to Button");
            return null;
        }

        // Set the button text
        if (jsonElement.TryGetProperty("text", out var text))
        {
            button.Content = text.GetString();
        }

        //if (element.TryGetProperty("command", out var command))
        //{
        //    button.Click += (sender, args) =>
        //    {
        //        Console.WriteLine($"Button command: {command.GetString()}");
        //    };
        //}

        return button;
    }

    private bool GetBindingPropertyPath(JsonElement jsonElement, string configPropertyName, out string bindingPropertyPath)
    {
        bindingPropertyPath = string.Empty;

        if (!jsonElement.TryGetProperty(configPropertyName, out var bindingConfig))
        {
            // Config does not contain the specified property.
            // The client decides if this is an error or not.
            return false;
        }

        var path = bindingConfig.GetString();
        if (string.IsNullOrEmpty(path))
        {
            _buildErrors.Add($"Binding property '{configPropertyName}' is empty");
            return false;
        }

        // Parsed the binding info successfully
        bindingPropertyPath = path;
        return true;
    }

    private void ApplyBinding<T>(
        FrameworkElement frameworkElement,
        DependencyProperty dependencyProperty,
        BindingMode bindingMode,
        string propertyPath) where T : IPropertyViewModel
    {
        if (_formDataProvider is null)
        {
            _buildErrors.Add($"Failed to apply property binding: '{propertyPath}'. Form data provider is null");
            return;
        }

        try
        {
            // Instantiate the property view model
            var viewModel = _serviceProvider.GetService<T>();
            if (viewModel is null)
            {
                _buildErrors.Add($"Failed to instantiate property view model: '{typeof(T).Name}'");
                return;
            }

            // Initialize the property view model
            var initResult = viewModel.Initialize(_formDataProvider, propertyPath);
            if (initResult.IsFailure)
            {
                _buildErrors.Add($"Failed to initialize property view model: '{typeof(T).Name}'");
                return;
            }

            // The DataContext will be used automatically as the binding source
            frameworkElement.DataContext = viewModel;

            // Tell the view model to stop listening for property changes when the view is unloaded
            frameworkElement.Unloaded += (s, e) =>
            {
                var vm = frameworkElement.DataContext as IPropertyViewModel;
                if (vm is not null)
                {
                    vm.OnViewUnloaded();
                    frameworkElement.DataContext = null;
                }
            };

            // Bind the dependency property to the property view model
            var binding = new Binding()
            {
                Path = new PropertyPath(viewModel.BoundPropertyName),
                Mode = bindingMode
            };
            frameworkElement.SetBinding(dependencyProperty, binding);
        }
        catch (Exception ex)
        {
            _buildErrors.Add($"An exception occurred when applying property binding: '{propertyPath}'. {ex}");
        }
    }

    private bool ValidateConfigKeys(JsonElement jsonElement, HashSet<string> validConfigKeys)
    {
        bool valid = true;
        var keys = new List<string>();
        if (jsonElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in jsonElement.EnumerateObject())
            {
                var configKey = property.Name;
                if (configKey == "element" ||
                    configKey == "horizontalAlignment" ||
                    configKey == "verticalAlignment")
                {
                    // Skip general config properties that apply to all elements
                    continue;
                }    

                if (!validConfigKeys.Contains(configKey))
                {
                    _buildErrors.Add($"Invalid form element property: '{configKey}'");
                    valid = false;
                }
            }
        }

        return valid;
    }

    private bool ApplyAlignmentConfig(FrameworkElement frameworkElement, JsonElement config)
    {
        if (config.TryGetProperty("horizontalAlignment", out var horizontalAlignment))
        {
            switch (horizontalAlignment.GetString())
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
                    _buildErrors.Add($"Invalid horizontal alignment value: '{horizontalAlignment.GetString()}'");
                    return false;
            }
        }

        if (config.TryGetProperty("verticalAlignment", out var verticalAlignment))
        {
            switch (verticalAlignment.GetString())
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
                    _buildErrors.Add($"Invalid vertical alignment value: '{horizontalAlignment.GetString()}'");
                    return false;
            }
        }

        return true;
    }
}
