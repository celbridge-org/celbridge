using System.ComponentModel;
using Celbridge.Dialog;
using Celbridge.Logging;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// Asks where to save a file downloaded in a web view. The first page saves the file to the download folder, and
/// the second page chooses that folder. The dialog stays open while the user switches between the pages.
/// </summary>
public sealed partial class DownloadFileDialog : ContentDialog, IDownloadFileDialog
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ILogger<DownloadFileDialog> _logger;
    private readonly IMessengerService _messengerService;

    private Button? _primaryButton;
    private bool _isFolderListInitialized;
    private DownloadFileAnswer? _automaticAnswer;

    public DownloadFileDialogViewModel ViewModel { get; }

    private string ProjectFolderString => _stringLocalizer.GetString("DownloadFileDialog_ProjectFolder");
    private string ChooseFolderString => _stringLocalizer.GetString("DownloadFileDialog_ChooseFolder");

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
        _automaticAnswer = null;
        _messengerService.Register<DialogAnswerMessage>(this, OnDialogAnswer);
        FolderList.ViewModel.PropertyChanged += OnFolderListPropertyChanged;
        try
        {
            var result = await ShowAsync();

            if (_automaticAnswer is not null)
            {
                return _automaticAnswer;
            }

            // Save is the only button that closes the dialog with the primary result. On the second page the
            // primary button is Select, which keeps the dialog open.
            if (result == ContentDialogResult.Primary)
            {
                return new DownloadFileAnswer(DownloadFileChoice.ProjectFolder, ViewModel.DownloadFolder);
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

    private void Dialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!ViewModel.IsChoosingFolder)
        {
            return;
        }

        args.Cancel = true;
        ChooseSelectedFolder();
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
            && _automaticAnswer is null)
        {
            args.Cancel = true;
            ShowFilePage();
        }
    }

    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        ShowFolderPage();
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
            _automaticAnswer = new DownloadFileAnswer(DownloadFileChoice.Cancel, ResourceKey.Empty);
            Hide();
            return;
        }

        _automaticAnswer = new DownloadFileAnswer(DownloadFileChoice.ProjectFolder, folder);
        _logger.LogInformation($"Download File dialog answered automatically with '{folder}'.");
        Hide();
    }
}
