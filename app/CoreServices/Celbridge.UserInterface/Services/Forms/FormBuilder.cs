using Celbridge.Forms;
using Celbridge.Logging;
using Celbridge.UserInterface.ViewModels.Forms;
using System.Text.Json;

namespace Celbridge.UserInterface.Services.Forms;

public class FormBuilder
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FormBuilder> _logger;

    private IFormDataProvider? _formDataProvider;
    public IFormDataProvider FormDataProvider => _formDataProvider!;

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

        Panel? formPanel = null;

        try
        {
            // Create the root panel for the form
            var uiElement = CreateFormElement(formConfig);
            if (uiElement is Panel panel &&
                panel is not null)
            {
                // Set the root panel's data context to the form data provider
                formPanel = panel;
                formPanel.DataContext = formDataProvider;
            }
            else
            {
                _buildErrors.Add("Failed to create form panel");
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

        Guard.IsNotNull(formPanel);

        formPanel.Loaded += (s, e) =>
        {
            var panel = s as Panel; // Avoid capturing references outside the lambda
            Guard.IsNotNull(panel);

            var formDataProvider = panel.DataContext as IFormDataProvider;
            if (formDataProvider is not null)
            {
                formDataProvider.OnFormLoaded();
            }
        };

        formPanel.Unloaded += (s, e) =>
        {
            var panel = s as Panel; // Avoid capturing references outside the lambda
            Guard.IsNotNull(panel);

            var formDataProvider = panel.DataContext as IFormDataProvider;
            if (formDataProvider is not null)
            {
                formDataProvider.OnFormUnloaded();
            }
        };

        return Result<object>.Ok(formPanel);
    }

    public UIElement? CreateFormElement(JsonElement config)
    {
        Guard.IsNotNull(_formDataProvider);

        if (config.ValueKind != JsonValueKind.Object)
        {
            _buildErrors.Add("Form element config is not an object");
            return null;
        }

        if (!config.TryGetProperty("element", out var element))
        {
            _buildErrors.Add("Form object does not contain an 'element' property");
            return null;
        }
        var elementName = element.GetString();

        UIElement? uiElement = null;
        switch (elementName)
        {
            case "StackPanel":
                var stackPanelResult = StackPanelElement.CreateStackPanel(config, this);
                if (stackPanelResult.IsFailure)
                {
                    _buildErrors.Add(stackPanelResult.Error);
                }
                else
                {
                    uiElement = stackPanelResult.Value;
                }
                break;

            case "TextBox":
                var textBoxResult = TextBoxElement.CreateTextBox(config, this);
                if (textBoxResult.IsFailure)
                {
                    _buildErrors.Add(textBoxResult.Error);
                }
                else
                {
                    uiElement = textBoxResult.Value;
                }
                break;

            case "TextBlock":
                var textBlockResult = TextBlockElement.CreateTextBlock(config, this);
                if (textBlockResult.IsFailure)
                {
                    _buildErrors.Add(textBlockResult.Error);
                }
                else
                {
                    uiElement = textBlockResult.Value;
                }
                break;

            case "DropDownTextBox":
                var dropDownTextBoxResult = DropDownTextBoxElement.CreateDropDownTextBox(config, this);
                if (dropDownTextBoxResult.IsFailure)
                {
                    _buildErrors.Add(dropDownTextBoxResult.Error);
                }
                else
                {
                    uiElement = dropDownTextBoxResult.Value;
                }
                break;

            case "Button":
                var buttonResult = ButtonElement.CreateButton(config, this);
                if (buttonResult.IsFailure)
                {
                    _buildErrors.Add(buttonResult.Error);
                }
                else
                {
                    uiElement = buttonResult.Value;
                }
                break;

            case "ComboBox":
                var comboBoxResult = ComboBoxElement.CreateComboBox(config, this);
                if (comboBoxResult.IsFailure)
                {
                    _buildErrors.Add(comboBoxResult.Error);
                }
                else
                {
                    uiElement = comboBoxResult.Value;
                }
                break;

            case "InfoBar":
                var infoBarResult = InfoBarElement.CreateInfoBar(config, this);
                if (infoBarResult.IsFailure)
                {
                    _buildErrors.Add(infoBarResult.Error);
                }
                else
                {
                    uiElement = infoBarResult.Value;
                }
                break;
        }

        if (uiElement is null)
        {
            _buildErrors.Add($"Failed to create element of type: '{elementName}'");
            return null;
        }

        return uiElement;
    }
}
