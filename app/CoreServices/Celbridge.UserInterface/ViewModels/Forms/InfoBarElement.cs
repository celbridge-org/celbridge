using Celbridge.UserInterface.Services.Forms;
using System.Text.Json;

namespace Celbridge.UserInterface.ViewModels.Forms;

public partial class InfoBarElement : FormElement
{
    public static Result<FrameworkElement> CreateInfoBar(JsonElement config, FormBuilder formBuilder)
    {
        var formElement = ServiceLocator.AcquireService<InfoBarElement>();
        return formElement.Create(config, formBuilder);
    }

    [ObservableProperty]
    private bool _isOpen;
    private PropertyBinder<bool>? _isOpenBinder;

    [ObservableProperty]
    private bool _isClosable = true;
    private PropertyBinder<bool>? _isClosableBinder;

    [ObservableProperty]
    private string _title = string.Empty;
    private PropertyBinder<string>? _titleBinder;

    [ObservableProperty]
    private string _message = string.Empty;
    private PropertyBinder<string>? _messageBinder;

    [ObservableProperty]
    private InfoBarSeverity _severity = InfoBarSeverity.Informational;
    private PropertyBinder<InfoBarSeverity>? _severityBinder;

    protected override Result<FrameworkElement> CreateUIElement(JsonElement config, FormBuilder formBuilder)
    {
        //
        // Create the UI element
        //

        var infoBar = new InfoBar();
        infoBar.DataContext = this;

        //
        // Check all specified config properties are supported
        //

        var validateResult = ValidateConfigKeys(config, new HashSet<string>()
        {
            "isOpen",
            "isClosable",
            "title",
            "message",
            "severity",
            "actionButton"
        });

        if (validateResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail("Invalid InfoBar configuration")
                .WithErrors(validateResult);
        }

        //
        // Apply common config properties
        //

        var commonConfigResult = ApplyCommonConfig(infoBar, config);
        if (commonConfigResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply common config properties")
                .WithErrors(commonConfigResult);
        }

        //
        // Apply element-specific config properties
        //

        var isOpenResult = ApplyIsOpenConfig(config, infoBar);
        if (isOpenResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'isOpen' config")
                .WithErrors(isOpenResult);
        }

        var isClosableResult = ApplyIsClosableConfig(config, infoBar);
        if (isClosableResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'isClosable' config")
                .WithErrors(isClosableResult);
        }

        var titleResult = ApplyTitleConfig(config, infoBar);
        if (titleResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'title' config")
                .WithErrors(titleResult);
        }

        var messageResult = ApplyMessageConfig(config, infoBar);
        if (messageResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'message' config")
                .WithErrors(messageResult);
        }

        var severityResult = ApplySeverityConfig(config, infoBar);
        if (severityResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'severity' config")
                .WithErrors(severityResult);
        }

        var buttonResult = ApplyActionButtonConfig(config, formBuilder, infoBar);
        if (buttonResult.IsFailure)
        {
            return Result<FrameworkElement>.Fail($"Failed to apply 'actionButton' config property")
                .WithErrors(buttonResult);
        }

        return Result<FrameworkElement>.Ok(infoBar);
    }

    private Result ApplyIsOpenConfig(JsonElement config, InfoBar infoBar)
    {
        if (config.TryGetProperty("isOpen", out var configValue))
        {
            if (configValue.IsBindingConfig())
            {
                _isOpenBinder = PropertyBinder<bool>.Create(infoBar, this)
                    .Binding(InfoBar.IsOpenProperty, BindingMode.TwoWay, nameof(IsOpen))
                    .Setter((value) => IsOpen = value)
                    .Getter(() => IsOpen);
  
                return _isOpenBinder.Initialize(configValue);
            }
            else if (configValue.ValueKind == JsonValueKind.True)
            {
                infoBar.IsOpen = true;
            }
            else if (configValue.ValueKind == JsonValueKind.False)
            {
                infoBar.IsOpen = false;
            }
            else
            {
                return Result<bool>.Fail("'isOpen' config is not valid");
            }
        }

        return Result.Ok();
    }

    private Result ApplyIsClosableConfig(JsonElement config, InfoBar infoBar)
    {
        if (config.TryGetProperty("isClosable", out var configValue))
        {
            if (configValue.IsBindingConfig())
            {
                _isClosableBinder = PropertyBinder<bool>.Create(infoBar, this)
                    .Binding(InfoBar.IsClosableProperty, BindingMode.OneWay, nameof(IsClosable))
                    .Setter((value) => IsClosable = value)
                    .Getter(() => IsClosable);

                return _isClosableBinder.Initialize(configValue);
            }
            else if (configValue.ValueKind == JsonValueKind.True)
            {
                infoBar.IsClosable = true;
            }
            else if (configValue.ValueKind == JsonValueKind.False)
            {
                infoBar.IsClosable = false;
            }
            else
            {
                return Result.Fail("'isClosable' config is not valid");
            }
        }

        return Result.Ok();
    }

    private Result ApplyTitleConfig(JsonElement config, InfoBar infoBar)
    {
        if (config.TryGetProperty("title", out var configValue))
        {
            if (configValue.IsBindingConfig())
            {
                _titleBinder = PropertyBinder<string>.Create(infoBar, this)
                    .Binding(InfoBar.TitleProperty, BindingMode.OneWay, nameof(Title))
                    .Setter((value) => Title = value)
                    .Getter(() => Title);

                return _titleBinder.Initialize(configValue);
            }
            else if (configValue.ValueKind == JsonValueKind.String)
            {
                infoBar.Title = configValue.GetString() ?? string.Empty;
            }
            else
            {
                return Result<bool>.Fail("'title' config is not valid");
            }
        }

        return Result.Ok();
    }

    private Result ApplyMessageConfig(JsonElement config, InfoBar infoBar)
    {
        if (config.TryGetProperty("message", out var configValue))
        {
            if (configValue.IsBindingConfig())
            {
                _messageBinder = PropertyBinder<string>.Create(infoBar, this)
                    .Binding(InfoBar.MessageProperty, BindingMode.OneWay, nameof(Message))
                    .Setter((value) => Message = value)
                    .Getter(() => Message);

                return _messageBinder.Initialize(configValue);
            }
            else if (configValue.ValueKind == JsonValueKind.String)
            {
                infoBar.Message = configValue.GetString() ?? string.Empty;
            }
            else
            {
                return Result<bool>.Fail("'message' config is not valid");
            }
        }

        return Result.Ok();
    }

    private Result ApplySeverityConfig(JsonElement config, InfoBar infoBar)
    {
        if (config.TryGetProperty("severity", out var configValue))
        {
            if (configValue.IsBindingConfig())
            {
                _severityBinder = PropertyBinder<InfoBarSeverity>.Create(infoBar, this)
                    .Binding(InfoBar.SeverityProperty, BindingMode.OneWay, nameof(Severity))
                    .Setter((value) =>
                    {
                        Severity = value;
                    })
                    .Getter(() =>
                    {
                        return Severity;
                    });

                return _severityBinder.Initialize(configValue);
            }
            else if (configValue.ValueKind == JsonValueKind.String)
            {
                var value = configValue.GetString() ?? nameof(InfoBarSeverity.Informational);
                infoBar.Severity = Enum.Parse<InfoBarSeverity>(value);
            }
            else
            {
                return Result<bool>.Fail("'severity' config is not valid");
            }
        }

        return Result.Ok();
    }

    private Result ApplyActionButtonConfig(JsonElement config, FormBuilder formBuilder, InfoBar infoBar)
    {
        if (config.TryGetProperty("actionButton", out var actionButtonProperty))
        {
            // Check the type
            if (actionButtonProperty.ValueKind != JsonValueKind.Object)
            {
                return Result.Fail("'actionButton' property must be an object");
            }

            var createButtonResult = ButtonElement.CreateButton(actionButtonProperty, formBuilder);
            if (createButtonResult.IsFailure)
            {
                return Result.Fail($"Failed to create action button")
                    .WithErrors(createButtonResult);
            }
            var actionButton = createButtonResult.Value as Button;

            Guard.IsNotNull(actionButton);

            infoBar.ActionButton = actionButton;
        }

        return Result.Ok();
    }

    protected override void OnFormDataChanged(string propertyPath)
    {
        _isOpenBinder?.OnFormDataChanged(propertyPath);
        _titleBinder?.OnFormDataChanged(propertyPath);
        _messageBinder?.OnFormDataChanged(propertyPath);
        _isClosableBinder?.OnFormDataChanged(propertyPath);
        _severityBinder?.OnFormDataChanged(propertyPath);
    }

    protected override void OnMemberDataChanged(string propertyName)
    {
        // Todo: In what situation do these get called?
        _isOpenBinder?.OnMemberDataChanged(propertyName);
        _titleBinder?.OnMemberDataChanged(propertyName);
        _messageBinder?.OnMemberDataChanged(propertyName);
        _isClosableBinder?.OnMemberDataChanged(propertyName);
        _severityBinder?.OnMemberDataChanged(propertyName);
    }

    protected override void OnElementUnloaded()
    {
        _isOpenBinder?.OnElementUnloaded();
        _titleBinder?.OnElementUnloaded();
        _messageBinder?.OnElementUnloaded();
        _isClosableBinder?.OnElementUnloaded();
        _severityBinder?.OnElementUnloaded();
    }
}
