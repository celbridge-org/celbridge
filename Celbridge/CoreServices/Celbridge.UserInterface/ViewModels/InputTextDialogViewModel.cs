using Celbridge.Validators;
using System.ComponentModel;

namespace Celbridge.UserInterface.ViewModels;

public partial class InputTextDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private string _headerText = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isTextValid = false;

    [ObservableProperty]
    private bool _isSubmitEnabled = false;

    public IValidator? Validator { get; set; }

    public InputTextDialogViewModel()
    {
        PropertyChanged += InputTextDialogViewModel_PropertyChanged;
    }

    private void InputTextDialogViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InputText))
        {
            UpdateValidationState();
        }
    }

    private void UpdateValidationState()
    {
        Guard.IsNotNull(Validator);

        var result = Validator.Validate(InputText);

        IsTextValid = result.IsValid;
        IsSubmitEnabled = IsTextValid && !string.IsNullOrEmpty(InputText);

        if (result.Errors.Count == 0)
        {
            ErrorText = string.Empty;
        }
        else
        {
            ErrorText = result.Errors[0];
        }
    }
}
