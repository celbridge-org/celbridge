using Celbridge.Dialog;
using Windows.System;

namespace Celbridge.UserInterface.Views;

public sealed partial class NewFileDialog : ContentDialog, INewFileDialog
{
    private readonly IStringLocalizer _stringLocalizer;

    public NewFileDialogViewModel ViewModel { get; }

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

    private LocalizedString CreateString => _stringLocalizer.GetString($"DialogButton_Create");
    private LocalizedString CancelString => _stringLocalizer.GetString($"DialogButton_Cancel");
    private LocalizedString FileTypeString => _stringLocalizer.GetString($"NewFileDialog_FileType");

    private bool _pressedEnter;
    private Range _selectionRange;

    public NewFileDialog()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<NewFileDialogViewModel>();

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

    public async Task<Result<NewFileConfig>> ShowDialogAsync()
    {
        var contentDialogResult = await ShowAsync();
        if (contentDialogResult == ContentDialogResult.Primary || _pressedEnter)
        {
            // Save the extension preference for next time
            ViewModel.SaveFileExtensionPreference();
            
            var config = new NewFileConfig(ViewModel.FileName, ViewModel.SelectedFileType);
            return Result<NewFileConfig>.Ok(config);
        }

        return Result<NewFileConfig>.Fail("Dialog was cancelled");
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
