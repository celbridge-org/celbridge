using System.ComponentModel;

namespace Celbridge.UserInterface.ViewModels;

public partial class SecretInputDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private string _headerText = string.Empty;

    [ObservableProperty]
    private string _secretText = string.Empty;

    [ObservableProperty]
    private bool _isSubmitEnabled = false;

    public SecretInputDialogViewModel()
    {
        PropertyChanged += SecretInputDialogViewModel_PropertyChanged;
    }

    private void SecretInputDialogViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SecretText))
        {
            IsSubmitEnabled = !string.IsNullOrEmpty(SecretText);
        }
    }
}
