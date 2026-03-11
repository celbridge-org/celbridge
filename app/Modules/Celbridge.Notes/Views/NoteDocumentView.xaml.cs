using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Host.Helpers;
using Celbridge.Logging;
using Celbridge.Notes.Services;
using Celbridge.Notes.ViewModels;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.WebView;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Microsoft.Web.WebView2.Core;
using StreamJsonRpc;

namespace Celbridge.Notes.Views;

public sealed partial class NoteDocumentView : WebView2DocumentView, IHostDocument, IHostDialog
{
    // Notes can take time to serialize, especially with embedded images
    private const int SaveRequestTimeoutSeconds = 30;

    private readonly ILogger _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;

    private NoteHost? _noteHost;

    // Track save result from async RPC callback
    private TaskCompletionSource<Result>? _saveResultTcs;

    public NoteDocumentViewModel ViewModel { get; }

    public override ResourceKey FileResource => ViewModel.FileResource;

    public NoteDocumentView(
        IServiceProvider serviceProvider,
        ILogger<NoteDocumentView> logger,
        ICommandService commandService,
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService,
        IWorkspaceWrapper workspaceWrapper,
        IStringLocalizer stringLocalizer,
        IDialogService dialogService,
        IWebViewFactory webViewFactory)
        : base(messengerService, webViewFactory)
    {
        ViewModel = serviceProvider.GetRequiredService<NoteDocumentViewModel>();

        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;
        _userInterfaceService = userInterfaceService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;

        this.InitializeComponent();

        // Set the container where the WebView will be placed
        WebViewContainer = NoteWebViewContainer;

        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChanged);

        Loaded += NoteDocumentView_Loaded;

        ViewModel.ReloadRequested += ViewModel_ReloadRequested;
    }

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
    }

    protected override async Task<Result> SaveDocumentContentAsync()
    {
        if (_noteHost is null)
        {
            _logger.LogDebug("Save skipped - NoteHost not initialized");
            return Result.Ok();
        }

        if (!TryBeginSave())
        {
            _logger.LogDebug("Save already in progress, queuing pending save");
            return Result.Ok();
        }

        // Set up completion source to receive the save result from SaveAsync
        _saveResultTcs = new TaskCompletionSource<Result>();

        // Request the JS side to save - it will call document/save
        // which triggers our SaveAsync handler
        await _noteHost.NotifyRequestSaveAsync();

        // Wait for SaveAsync to complete, with timeout to prevent hanging
        var timeout = TimeSpan.FromSeconds(SaveRequestTimeoutSeconds);
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(_saveResultTcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            _saveResultTcs = null;
            CompleteSave();

            var errorMessage = $"Note editor failed to respond within {SaveRequestTimeoutSeconds} seconds. " +
                               $"The editor may be in an unstable state. File: {ViewModel.FilePath}";

            _logger.LogError(errorMessage);

            return Result.Fail(errorMessage);
        }

        var result = await _saveResultTcs.Task;
        _saveResultTcs = null;

        return result;
    }

    private async void NoteDocumentView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= NoteDocumentView_Loaded;

        await InitNoteViewAsync();
    }

    private async Task InitNoteViewAsync()
    {
        try
        {
            // Acquire WebView from factory and add to container
            await AcquireWebViewAsync();

            // Set up virtual host mapping for Note editor assets
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "note.celbridge",
                "Celbridge.Notes/Web/note",
                CoreWebView2HostResourceAccessKind.Allow);

            // Map the project folder so resource key image paths resolve correctly
            var projectFolder = _resourceRegistry.ProjectFolderPath;
            if (!string.IsNullOrEmpty(projectFolder))
            {
                WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "project.celbridge",
                    projectFolder,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            // Sync WebView2 color scheme with the app theme
            ApplyThemeToWebView();

            var settings = WebView.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = true;
            settings.AreDefaultContextMenusEnabled = true;

            // Cancel all navigations except the initial editor page load
            WebView.NavigationStarting += (s, args) =>
            {
                var uri = args.Uri;
                if (string.IsNullOrEmpty(uri))
                {
                    return;
                }

                if (uri.StartsWith("https://note.celbridge/index.html"))
                {
                    return;
                }

                args.Cancel = true;
            };

            // Block all new window requests
            WebView.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                args.Handled = true;
            };

            // Initialize the host
            InitializeHost();

            if (Host is null)
            {
                _logger.LogError("Failed to initialize host");
                return;
            }

            // Create the Note-specific host wrapper
            _noteHost = new NoteHost(Host);

            // Register this view as the handler for additional RPC interfaces
            _noteHost.AddLocalRpcTarget<IHostDocument>(this);
            _noteHost.AddLocalRpcTarget<IHostDialog>(this);

            _noteHost.StartListening();

            // Navigate to the editor
            WebView.CoreWebView2.Navigate("https://note.celbridge/index.html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Note Web View.");
        }
    }

    private DocumentMetadata CreateMetadata()
    {
        return new DocumentMetadata(
            ViewModel.FilePath,
            ViewModel.FileResource.ToString(),
            Path.GetFileName(ViewModel.FilePath));
    }

    #region IHostDocument

    public async Task<InitializeResult> InitializeAsync(string protocolVersion)
    {
        // Validate protocol version
        if (protocolVersion != "1.0")
        {
            throw new LocalRpcException($"Unsupported protocol version: {protocolVersion}. Expected: 1.0");
        }

        // Load content from file
        var content = await ViewModel.LoadNoteContent();

        var metadata = CreateMetadata();

        // Gather localization strings
        var localization = WebViewLocalizationHelper.GetLocalizedStrings(_stringLocalizer, "Note_");

        return new InitializeResult(content, metadata, localization);
    }

    public async Task<SaveResult> SaveAsync(string content)
    {
        try
        {
            var saveResult = await ViewModel.SaveNoteToFile(content);

            if (saveResult.IsFailure)
            {
                _logger.LogError(saveResult, "Failed to save note data");
                CompleteSave();
                _saveResultTcs?.TrySetResult(saveResult);
                return new SaveResult(false, saveResult.Error);
            }

            // Reset the ViewModel's save state flags
            ViewModel.OnSaveCompleted();

            // Check if there's a pending save that needs processing
            if (CompleteSave())
            {
                _logger.LogDebug("Processing pending save request");
                ViewModel.OnDataChanged();
            }

            _saveResultTcs?.TrySetResult(Result.Ok());
            return new SaveResult(true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during save");
            CompleteSave();
            var failResult = Result.Fail("Exception during save").WithException(ex);
            _saveResultTcs?.TrySetResult(failResult);
            return new SaveResult(false, ex.Message);
        }
    }

    public async Task<LoadResult> LoadAsync()
    {
        var content = await ViewModel.LoadNoteContent();
        var metadata = CreateMetadata();

        return new LoadResult(content, metadata);
    }

    #endregion

    #region IHostDialog

    public async Task<PickImageResult> PickImageAsync(IReadOnlyList<string>? extensions = null)
    {
        var extensionsArray = extensions?.ToArray();
        if (extensionsArray is null || extensionsArray.Length == 0)
        {
            extensionsArray =
            [
                ".png",
                ".jpg",
                ".jpeg",
                ".gif",
                ".webp",
                ".svg",
                ".bmp"
            ];
        }

        var title = _stringLocalizer.GetString("Note_SelectImage_Title");
        var result = await _dialogService.ShowResourcePickerDialogAsync(extensionsArray, title, showPreview: true);

        if (result.IsSuccess)
        {
            var resourceKey = result.Value.ToString();
            var relativePath = ViewModel.GetRelativePathFromResourceKey(resourceKey);
            return new PickImageResult(relativePath);
        }

        return new PickImageResult(null);
    }

    public async Task<PickFileResult> PickFileAsync(IReadOnlyList<string>? extensions = null)
    {
        var title = _stringLocalizer.GetString("Note_SelectFile_Title");
        var extensionsArray = extensions?.ToArray() ?? [];
        var result = await _dialogService.ShowResourcePickerDialogAsync(extensionsArray, title);

        if (result.IsSuccess)
        {
            var resourceKey = result.Value.ToString();
            var relativePath = ViewModel.GetRelativePathFromResourceKey(resourceKey);
            return new PickFileResult(relativePath);
        }

        return new PickFileResult(null);
    }

    public Task<AlertResult> AlertAsync(string title, string message)
    {
        throw new NotSupportedException("Alert dialog is not used by the Note editor.");
    }

    #endregion

    private void OpenSystemBrowser(string uri)
    {
        _commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = uri;
        });
    }

    #region IHostDocument

    public void OnDocumentChanged()
    {
        ViewModel.OnDataChanged();
    }

    #endregion

    #region IHostInput

    public void OnLinkClicked(string href)
    {
        if (string.IsNullOrEmpty(href))
        {
            return;
        }

        var resolveResult = ViewModel.ResolveLinkTarget(href);

        if (resolveResult.IsFailure)
        {
            _logger.LogWarning($"Failed to resolve link: {href}");
            _ = ShowLinkErrorAsync(href);
            return;
        }

        var resourceKey = resolveResult.Value;

        if (resourceKey.IsEmpty)
        {
            // External URL
            OpenSystemBrowser(href);
        }
        else
        {
            // Internal resource
            _commandService.Execute<IOpenDocumentCommand>(command =>
            {
                command.FileResource = resourceKey;
            });
        }
    }

    #endregion

    private async Task ShowLinkErrorAsync(string href)
    {
        var errorTitle = _stringLocalizer.GetString("Note_LinkError_Title");
        var errorMessage = _stringLocalizer.GetString("Note_LinkError_Message", href);
        await _dialogService.ShowAlertDialogAsync(errorTitle, errorMessage);
    }

    public override async Task<Result> SetFileResource(ResourceKey fileResource)
    {
        var filePath = _resourceRegistry.GetResourcePath(fileResource);

        if (_resourceRegistry.GetResource(fileResource).IsFailure)
        {
            return Result.Fail($"File resource does not exist in resource registry: {fileResource}");
        }

        if (!File.Exists(filePath))
        {
            return Result.Fail($"File resource does not exist on disk: {fileResource}");
        }

        ViewModel.FileResource = fileResource;
        ViewModel.FilePath = filePath;

        await Task.CompletedTask;

        return Result.Ok();
    }

    public override async Task<Result> LoadContent()
    {
        return await ViewModel.LoadContent();
    }

    public override async Task PrepareToClose()
    {
        _messengerService.UnregisterAll(this);

        Loaded -= NoteDocumentView_Loaded;

        ViewModel.ReloadRequested -= ViewModel_ReloadRequested;

        ViewModel.Cleanup();

        await base.PrepareToClose();
    }

    private void OnThemeChanged(object recipient, ThemeChangedMessage message)
    {
        if (WebView?.CoreWebView2 is not null)
        {
            ApplyThemeToWebView();
        }
    }

    private void ApplyThemeToWebView()
    {
        var theme = _userInterfaceService.UserInterfaceTheme;
        WebView!.CoreWebView2.Profile.PreferredColorScheme = theme == UserInterfaceTheme.Dark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;
    }

    private async void ViewModel_ReloadRequested(object? sender, EventArgs e)
    {
        // External file change detected - notify JS to reload
        // The dirty state conflict handling is done in the ViewModel before raising this event
        if (_noteHost is not null)
        {
            await _noteHost.NotifyExternalChangeAsync();
        }
    }
}
