using System.ComponentModel;

namespace Celbridge.UserInterface.ViewModels;

public partial class InputTextDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private string _headerText = string.Empty;

    [ObservableProperty]
    private char[] _invalidCharacters = {};

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isTextValid = false;

    [ObservableProperty]
    private bool _isSubmitEnabled = false;

    public InputTextDialogViewModel()
    {
        UpdateValidationState();

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
        bool isValid = true;
        if (InvalidCharacters.Length > 0)
        {
            foreach (char c in InputText)
            {
                if (InvalidCharacters.Contains(c))
                {
                    isValid = false;
                }
            }
        }
        IsTextValid = isValid;
        IsSubmitEnabled = IsTextValid && !string.IsNullOrEmpty(InputText);
    }
}
