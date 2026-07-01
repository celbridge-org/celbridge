using Celbridge.Dialog;
using Windows.System;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// Display item for file types in the ComboBox, containing the display name and extension for icon lookup.
/// </summary>
public record FileTypeDisplayItem(string DisplayName, string Extension);

public sealed partial class NewFileDialog : ContentDialog, INewFileDialog
{
    private readonly IStringLocalizer _stringLocalizer;

    public NewFileDialogViewModel ViewModel { get; }

    private string TitleString => _stringLocalizer.GetString("NewFileDialog_NewFile");
    private string HeaderString => _stringLocalizer.GetString("NewFileDialog_FileName");
    private string CreateString => _stringLocalizer.GetString($"DialogButton_Create");
    private string CancelString => _stringLocalizer.GetString($"DialogButton_Cancel");
    private string FileTypeString => _stringLocalizer.GetString($"NewFileDialog_FileType");

    private bool _pressedEnter;
    private Range _selectionRange;

    public NewFileDialog()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<NewFileDialogViewModel>();

        this.InitializeComponent();

        // Populate file type dropdown with display items containing extension info for icons
        FileTypeComboBox.ItemsSource = ViewModel.FileTypes
            .Select(ft => new FileTypeDisplayItem(ft.DisplayName, ft.Extension))
            .ToList();

        this.EnableThemeSync();
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
