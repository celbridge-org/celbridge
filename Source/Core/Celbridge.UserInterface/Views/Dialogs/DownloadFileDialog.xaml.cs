using System.ComponentModel;
using Celbridge.Dialog;
using Celbridge.Logging;
using Microsoft.UI.Xaml.Input;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// Asks where to save a file downloaded in a web view. The first page saves the file to the download folder, or
/// opens the system save dialog for Other Location. The second page chooses the download folder. The dialog stays
/// open while the user switches between the pages, and beneath the system save dialog.
/// </summary>
public sealed partial class DownloadFileDialog : ContentDialog, IDownloadFileDialog
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ILogger<DownloadFileDialog> _logger;
    private readonly IMessengerService _messengerService;

    private Button? _primaryButton;
    private bool _isFolderListInitialized;
    private bool _isPickingOtherLocation;

    // Set when something other than the dialog's buttons settles it: an automatic answer, or the path the system
    // save dialog returned.
    private DownloadFileAnswer? _answer;

    public DownloadFileDialogViewModel ViewModel { get; }

    private string InProjectString => _stringLocalizer.GetString("DownloadFileDialog_InProject");
    private string ChooseFolderString => _stringLocalizer.GetString("DownloadFileDialog_ChooseFolder");
    private string OtherLocationString => _stringLocalizer.GetString("DownloadFileDialog_OtherLocation");
    private string OtherLocationHintString => _stringLocalizer.GetString("DownloadFileDialog_OtherLocationHint");

    public DownloadFileDialog()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _logger = ServiceLocator.AcquireService<ILogger<DownloadFileDialog>>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<DownloadFileDialogViewModel>();

        this.InitializeComponent();
        this.EnableThemeSync();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _primaryButton = GetTemplateChild("PrimaryButton") as Button;
    }

    public async Task<DownloadFileAnswer> ShowDialogAsync()
    {
        _answer = null;
        _messengerService.Register<DialogAnswerMessage>(this, OnDialogAnswer);
        FolderList.ViewModel.PropertyChanged += OnFolderListPropertyChanged;
        try
        {
            var result = await ShowAsync();

            if (_answer is not null)
            {
                return _answer;
            }

            // A primary result means Save with In Project selected.
            if (result == ContentDialogResult.Primary)
            {
                return new DownloadFileAnswer(DownloadFileChoice.InProject, ViewModel.DownloadFolder);
            }

            return new DownloadFileAnswer(DownloadFileChoice.Cancel, ResourceKey.Empty);
        }
        finally
        {
            FolderList.ViewModel.PropertyChanged -= OnFolderListPropertyChanged;
            _messengerService.UnregisterAll(this);
        }
    }

    private void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        // Save takes the keyboard, so Enter saves the file to the download folder.
        _primaryButton?.Focus(FocusState.Programmatic);
    }

    // ContentDialog makes one programmatic move of the keyboard to the dialog's first control, either before or after
    // Opened. This handler sends that move to Save. A click or Tab on In Project has a different focus state, so that
    // click or Tab still reaches In Project.
    private void InProjectRadioButton_GettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        if (args.FocusState == FocusState.Programmatic && _primaryButton is not null)
        {
            args.TrySetNewFocusedElement(_primaryButton);
        }
    }

    private void Dialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (ViewModel.IsChoosingFolder)
        {
            args.Cancel = true;
            ChooseSelectedFolder();
            return;
        }

        if (ViewModel.IsOtherLocationSelected)
        {
            args.Cancel = true;
            _ = SaveToOtherLocationAsync();
        }
    }

    private void Dialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!ViewModel.IsChoosingFolder)
        {
            return;
        }

        args.Cancel = true;
        ShowFilePage();
    }

    // Escape on the second page returns to the first page. An automatic answer closes the dialog from either
    // page.
    private void Dialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (ViewModel.IsChoosingFolder
            && args.Result == ContentDialogResult.None
            && _answer is null)
        {
            args.Cancel = true;
            ShowFilePage();
        }
    }

    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsOtherLocationSelected = false;
        ShowFolderPage();
    }

    private void InProjectRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        ViewModel.IsOtherLocationSelected = false;
    }

    private void OtherLocationRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        ViewModel.IsOtherLocationSelected = true;
    }

    // The dialog stays open beneath the system save dialog. A path chosen there closes the dialog. Canceling the
    // system save dialog returns to this dialog, where Cancel stops the download.
    private async Task SaveToOtherLocationAsync()
    {
        if (_isPickingOtherLocation)
        {
            return;
        }
        _isPickingOtherLocation = true;

        var pickedPath = string.Empty;
        try
        {
            var pickResult = await ViewModel.PickOtherLocationAsync();
            if (pickResult.IsSuccess)
            {
                pickedPath = pickResult.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show the system save dialog for a download");
        }
        finally
        {
            _isPickingOtherLocation = false;
        }

        // An automatic answer that arrived meanwhile has already closed the dialog.
        if (_answer is not null)
        {
            return;
        }

        if (string.IsNullOrEmpty(pickedPath))
        {
            DispatcherQueue.TryEnqueue(() => _primaryButton?.Focus(FocusState.Programmatic));
            return;
        }

        _answer = new DownloadFileAnswer(DownloadFileChoice.OtherLocation, ResourceKey.Empty, pickedPath);
        Hide();
    }

    private void FolderList_ItemInvoked(object? sender, EventArgs e)
    {
        ChooseSelectedFolder();
    }

    private void FolderList_EscapePressed(object? sender, EventArgs e)
    {
        ShowFilePage();
    }

    private void OnFolderListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResourcePickerDialogViewModel.IsSubmitEnabled))
        {
            UpdatePrimaryButton();
        }
    }

    private void ShowFolderPage()
    {
        // The project is walked only when the folder list is first shown, so the first page opens at once.
        if (!_isFolderListInitialized)
        {
            FolderList.ViewModel.InitializeForFolders(listProjectFolder: true);
            _isFolderListInitialized = true;
        }

        FolderList.ViewModel.SearchText = string.Empty;
        ViewModel.ShowFolderPage();
        UpdatePrimaryButton();

        // Focused once the page has been laid out, since a control that has just become visible cannot take the
        // keyboard yet.
        DispatcherQueue.TryEnqueue(() => FolderList.FocusSearchBox());

        _ = SelectDownloadFolderAsync();
    }

    private async Task SelectDownloadFolderAsync()
    {
        try
        {
            await FolderList.ViewModel.LoadItemsAsync();
            FolderList.SelectAndShow(ViewModel.DownloadFolder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to select the download folder in the folder list");
        }
    }

    private void ShowFilePage()
    {
        ViewModel.ShowFilePage();
        UpdatePrimaryButton();

        DispatcherQueue.TryEnqueue(() => _primaryButton?.Focus(FocusState.Programmatic));
    }

    private void ChooseSelectedFolder()
    {
        var selectedItem = FolderList.ViewModel.SelectedItem;
        if (selectedItem is null)
        {
            return;
        }

        ViewModel.SetDownloadFolder(selectedItem.ResourceKey);
        ShowFilePage();
    }

    private void UpdatePrimaryButton()
    {
        IsPrimaryButtonEnabled = !ViewModel.IsChoosingFolder
            || FolderList.ViewModel.IsSubmitEnabled;
    }

    private void OnDialogAnswer(object recipient, DialogAnswerMessage message)
    {
        if (message.Kind != DialogKind.DownloadFile)
        {
            return;
        }

        if (!ResourceKey.TryCreate(message.Payload, out var folder))
        {
            _logger.LogWarning(
                $"Download File auto-answer failed: payload '{message.Payload}' is not a valid resource key.");
            _answer = new DownloadFileAnswer(DownloadFileChoice.Cancel, ResourceKey.Empty);
            Hide();
            return;
        }

        _answer = new DownloadFileAnswer(DownloadFileChoice.InProject, folder);
        _logger.LogInformation($"Download File dialog answered automatically with '{folder}'.");
        Hide();
    }
}
