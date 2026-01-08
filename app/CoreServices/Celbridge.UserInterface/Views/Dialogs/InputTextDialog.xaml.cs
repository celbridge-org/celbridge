using Celbridge.Dialog;
using Windows.System;

namespace Celbridge.UserInterface.Views;

public sealed partial class InputTextDialog : ContentDialog, IInputTextDialog
{
    private readonly IStringLocalizer _stringLocalizer;

    public InputTextDialogViewModel ViewModel { get; }

    public string TitleText
    {
        get => ViewModel.TitleText;
        set => ViewModel.TitleText = value;
    }

    public string HeaderText
    {
        get => ViewModel.HeaderText;
        set => ViewModel.HeaderText = value;
    }

    private string OkString => _stringLocalizer.GetString($"DialogButton_Ok");
    private string CancelString => _stringLocalizer.GetString($"DialogButton_Cancel");

    private bool _pressedEnter;

    public InputTextDialog()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<InputTextDialogViewModel>();

        this.InitializeComponent();
    }

    private void InputTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            // Set a flag so that we can tell that the user pressed Enter
            _pressedEnter = true;
            Hide();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            Hide();
        }
    }

    public async Task<Result<string>> ShowDialogAsync()
    {
        var contentDialogResult = await ShowAsync();
        if (contentDialogResult == ContentDialogResult.Primary || _pressedEnter)
        {
            return Result<string>.Ok(ViewModel.InputText);
        }

        return Result<string>.Fail("Failed to input text");
    }

    public void SetDefaultText(string defaultText, Range selectionRange)
    {
        ViewModel.InputText = defaultText;
        InputTextBox.Text = defaultText;

        // Todo: This appears to have no effect on Skia.Gtk platform

        var (offset, length) = selectionRange.GetOffsetAndLength(defaultText.Length);
        InputTextBox.Select(offset, length);
    }
}
