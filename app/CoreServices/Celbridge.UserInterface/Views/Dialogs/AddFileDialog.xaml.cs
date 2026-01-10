using Celbridge.Dialog;
using Windows.System;

namespace Celbridge.UserInterface.Views;

public sealed partial class AddFileDialog : ContentDialog, IAddFileDialog
{
    private readonly IStringLocalizer _stringLocalizer;

    public AddFileDialogViewModel ViewModel { get; }

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

    private string CreateString => _stringLocalizer.GetString($"DialogButton_Create");
    private string CancelString => _stringLocalizer.GetString($"DialogButton_Cancel");
    private string FileTypeString => _stringLocalizer.GetString($"AddFileDialog_FileType");

    private bool _pressedEnter;
    private Range _selectionRange;

    public AddFileDialog()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<AddFileDialogViewModel>();

        this.InitializeComponent();

        // Populate file type dropdown
        FileTypeComboBox.ItemsSource = ViewModel.FileTypes.Select(ft => ft.DisplayName).ToList();
    }

    private void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        // Set focus to the text box when the dialog is opened
        FileNameTextBox.Focus(FocusState.Programmatic);
        ApplyTextSelection();
    }

    private void FileNameTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.IsSubmitEnabled)
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

    public async Task<Result<AddFileConfig>> ShowDialogAsync()
    {
        var contentDialogResult = await ShowAsync();
        if (contentDialogResult == ContentDialogResult.Primary || _pressedEnter)
        {
            // Save the extension preference for next time
            ViewModel.SaveFileExtensionPreference();
            
            var config = new AddFileConfig(ViewModel.FileName, ViewModel.SelectedFileType);
            return Result<AddFileConfig>.Ok(config);
        }

        return Result<AddFileConfig>.Fail("Dialog was cancelled");
    }

    public void SetDefaultFileName(string defaultFileName, Range selectionRange)
    {
        _selectionRange = selectionRange;
        ViewModel.SetDefaultFileName(defaultFileName, selectionRange);
        FileNameTextBox.Text = defaultFileName;
    }

    private void ApplyTextSelection()
    {
        var text = FileNameTextBox.Text;
        if (!string.IsNullOrEmpty(text))
        {
            var (offset, length) = _selectionRange.GetOffsetAndLength(text.Length);
            FileNameTextBox.Select(offset, length);
        }
    }
}
