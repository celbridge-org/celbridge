using Celbridge.Dialog;
using Windows.System;

namespace Celbridge.UserInterface.ViewModels;

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

    private TextBox _fileNameTextBox;
    private bool _pressedEnter;
    private Range _selectionRange;

    public NewFileDialog()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<NewFileDialogViewModel>();

        // File type dropdown
        var fileTypeComboBox = new ComboBox()
            .Header(FileTypeString)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .ItemsSource(ViewModel.FileTypes.Select(ft => ft.DisplayName).ToList())
            .SelectedIndex(x => x.Binding(() => ViewModel.SelectedFileTypeIndex)
                .Mode(BindingMode.TwoWay));

        // File name text box
        _fileNameTextBox = new TextBox()
            .Header
            (
                x => x.Binding(() => ViewModel.HeaderText)
                      .Mode(BindingMode.OneWay)
            )
            .Text
            (
                x => x.Binding(() => ViewModel.FileName)
                      .Mode(BindingMode.TwoWay)
                      .UpdateSourceTrigger(UpdateSourceTrigger.PropertyChanged)
            )
            .IsSpellCheckEnabled(false)
            .AcceptsReturn(false)
            .Margin(0, 12, 0, 0);

        _fileNameTextBox.KeyDown += FileNameTextBox_KeyDown;

        // Error text
        var errorText = new TextBlock()
            .Text
            (
                x => x.Binding(() => ViewModel.ErrorText)
                      .Mode(BindingMode.OneWay)
            )
            .Foreground(ThemeResource.Get<Brush>("ErrorTextBrush"))
            .Margin(6, 4, 0, 0)
            .Opacity
            (
                x => x.Binding(() => ViewModel.IsFileNameValid)
                      .Mode(BindingMode.OneWay)
                      .Convert((valid) => valid ? 0 : 1)
            );

        this.DataContext
        (
            ViewModel, (dialog, vm) => dialog
            .Title(x => x.Binding(() => ViewModel.TitleText).Mode(BindingMode.OneWay))
            .PrimaryButtonText(CreateString)
            .SecondaryButtonText(CancelString)
            .IsPrimaryButtonEnabled(x => x.Binding(() => ViewModel.IsSubmitEnabled).Mode(BindingMode.OneWay))
            .Content
            (
                new StackPanel()
                    .Orientation(Orientation.Vertical)
                    .HorizontalAlignment(HorizontalAlignment.Stretch)
                    .VerticalAlignment(VerticalAlignment.Center)
                    .MinWidth(350)
                    .Children
                    (
                        fileTypeComboBox,
                        _fileNameTextBox,
                        errorText
                    )
            )
        );

        // Set focus to the text box when the dialog is opened
        Opened += (s, e) =>
        {
            _fileNameTextBox.Focus(FocusState.Programmatic);
            ApplyTextSelection();
        };
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
        _fileNameTextBox.Text = defaultFileName;
    }

    private void ApplyTextSelection()
    {
        var text = _fileNameTextBox.Text;
        if (!string.IsNullOrEmpty(text))
        {
            var (offset, length) = _selectionRange.GetOffsetAndLength(text.Length);
            _fileNameTextBox.Select(offset, length);
        }
    }
}

