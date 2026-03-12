using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Extensions;
using Celbridge.Host;
using Celbridge.Host.Helpers;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.WebView;
using Microsoft.Extensions.Localization;
using Microsoft.Web.WebView2.Core;
using StreamJsonRpc;

namespace Celbridge.Documents.Extensions;

/// <summary>
/// Document view for custom (WebView2-based) extension editors.
/// Configured from an ExtensionManifest, handles the IHostDocument protocol,
/// and inherits SetFileResource, theme syncing, CreateMetadata, and save tracking from the base.
/// Optionally implements IHostDialog and IHostInput based on manifest capabilities.
/// </summary>
public sealed partial class ExtensionDocumentView : WebViewDocumentView, IHostDocument, IHostDialog
{
    private const int SaveRequestTimeoutSeconds = 30;

    private readonly ILogger<ExtensionDocumentView> _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;

    private readonly ExtensionDocumentViewModel _viewModel;

    // Track save result from async RPC callback
    private TaskCompletionSource<Result>? _saveResultTcs;

    protected override DocumentViewModel DocumentViewModel => _viewModel;

    /// <summary>
    /// The extension manifest that configures this view.
    /// Must be set before LoadContent() is called.
    /// </summary>
    public ExtensionManifest? Manifest { get; set; }

    public ExtensionDocumentView(
        IServiceProvider serviceProvider,
        ILogger<ExtensionDocumentView> logger,
        ICommandService commandService,
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService,
        IStringLocalizer stringLocalizer,
        IDialogService dialogService,
        IWebViewFactory webViewFactory)
        : base(messengerService, webViewFactory)
    {
        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;

        _viewModel = serviceProvider.GetRequiredService<ExtensionDocumentViewModel>();

        this.InitializeComponent();

        WebViewContainer = ExtensionWebViewContainer;

        EnableThemeSyncing(userInterfaceService);

        Loaded += ExtensionDocumentView_Loaded;

        _viewModel.ReloadRequested += ViewModel_ReloadRequested;
    }

    public override bool HasUnsavedChanges => _viewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return _viewModel.UpdateSaveTimer(deltaTime);
    }

    protected override async Task<Result> SaveDocumentContentAsync()
    {
        if (Host is null)
        {
            _logger.LogDebug("Save skipped - Host not initialized");
            return Result.Ok();
        }

        if (!TryBeginSave())
        {
            _logger.LogDebug("Save already in progress, queuing pending save");
            return Result.Ok();
        }

        _saveResultTcs = new TaskCompletionSource<Result>();

        await Host.NotifyRequestSaveAsync();

        var timeout = TimeSpan.FromSeconds(SaveRequestTimeoutSeconds);
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(_saveResultTcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            _saveResultTcs = null;
            CompleteSave();

            var errorMessage = $"Extension editor failed to respond within {SaveRequestTimeoutSeconds} seconds. " +
                               $"File: {_viewModel.FilePath}";

            _logger.LogError(errorMessage);

            return Result.Fail(errorMessage);
        }

        var result = await _saveResultTcs.Task;
        _saveResultTcs = null;

        return result;
    }

    private async void ExtensionDocumentView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ExtensionDocumentView_Loaded;

        await InitExtensionViewAsync();
    }

    private async Task InitExtensionViewAsync()
    {
        if (Manifest is null)
        {
            _logger.LogError("Cannot initialize extension view: Manifest is not set");
            return;
        }

        // Pass the manifest to the ViewModel for template content loading
        _viewModel.Manifest = Manifest;

        try
        {
            await AcquireWebViewAsync();

            // Map the extension's asset directory to a virtual host
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                Manifest.HostName,
                Manifest.ExtensionDirectory,
                CoreWebView2HostResourceAccessKind.Allow);

            // Map the project folder for resource key path resolution
            var projectFolder = ResourceRegistry.ProjectFolderPath;
            if (!string.IsNullOrEmpty(projectFolder))
            {
                WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "project.celbridge",
                    projectFolder,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            ApplyThemeToWebView();

            // Block all navigations except the extension's own host name
            var allowedHostPrefix = $"https://{Manifest.HostName}/";
            WebView.NavigationStarting += (s, args) =>
            {
                var uri = args.Uri;
                if (string.IsNullOrEmpty(uri))
                {
                    return;
                }

                if (uri.StartsWith(allowedHostPrefix))
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

            InitializeHost();

            if (Host is null)
            {
                _logger.LogError("Failed to initialize host for extension");
                return;
            }

            Host.AddLocalRpcTarget<IHostDocument>(this);

            // Register optional capabilities based on manifest
            var capabilities = Manifest.Capabilities;
            if (capabilities.Contains("dialog"))
            {
                Host.AddLocalRpcTarget<IHostDialog>(this);
            }

            StartHostListener();

            // Navigate to the extension's entry point
            var entryPoint = Manifest.EntryPoint ?? "index.html";
            var entryUrl = $"https://{Manifest.HostName}/{entryPoint}";
            WebView.CoreWebView2.Navigate(entryUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to initialize extension view: {Manifest.Name}");
        }
    }

    #region IHostDocument

    public async Task<InitializeResult> InitializeAsync(string protocolVersion)
    {
        DocumentRpcMethods.ValidateProtocolVersion(protocolVersion);

        var content = await _viewModel.LoadTextContentAsync();
        var metadata = CreateDocumentMetadata();

        var localization = LoadLocalizationStrings();

        return new InitializeResult(content, metadata, localization);
    }

    public async Task<LoadResult> LoadAsync()
    {
        var content = await _viewModel.LoadTextContentAsync();
        var metadata = CreateDocumentMetadata();

        return new LoadResult(content, metadata);
    }

    public async Task<SaveResult> SaveAsync(string content)
    {
        try
        {
            var saveResult = await _viewModel.SaveTextContentAsync(content);

            if (saveResult.IsFailure)
            {
                _logger.LogError(saveResult, "Failed to save extension document");
                CompleteSave();
                _saveResultTcs?.TrySetResult(saveResult);
                return new SaveResult(false, saveResult.Error);
            }

            _viewModel.OnSaveCompleted();

            if (CompleteSave())
            {
                _logger.LogDebug("Processing pending save request");
                _viewModel.OnDataChanged();
            }

            _saveResultTcs?.TrySetResult(Result.Ok());
            return new SaveResult(true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during extension save");
            CompleteSave();
            var failResult = Result.Fail("Exception during save").WithException(ex);
            _saveResultTcs?.TrySetResult(failResult);
            return new SaveResult(false, ex.Message);
        }
    }

    public void OnDocumentChanged()
    {
        _viewModel.OnDataChanged();
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

        var title = _stringLocalizer.GetString("Extension_SelectImage_Title");
        var result = await _dialogService.ShowResourcePickerDialogAsync(extensionsArray, title, showPreview: true);

        if (result.IsSuccess)
        {
            var resourceKey = result.Value.ToString();
            var relativePath = _viewModel.GetRelativePathFromResourceKey(resourceKey);
            return new PickImageResult(relativePath);
        }

        return new PickImageResult(null);
    }

    public async Task<PickFileResult> PickFileAsync(IReadOnlyList<string>? extensions = null)
    {
        var title = _stringLocalizer.GetString("Extension_SelectFile_Title");
        var extensionsArray = extensions?.ToArray() ?? [];
        var result = await _dialogService.ShowResourcePickerDialogAsync(extensionsArray, title);

        if (result.IsSuccess)
        {
            var resourceKey = result.Value.ToString();
            var relativePath = _viewModel.GetRelativePathFromResourceKey(resourceKey);
            return new PickFileResult(relativePath);
        }

        return new PickFileResult(null);
    }

    public async Task<AlertResult> AlertAsync(string title, string message)
    {
        await _dialogService.ShowAlertDialogAsync(title, message);
        return new AlertResult();
    }

    #endregion

    #region IHostInput (OnLinkClicked)

    public void OnLinkClicked(string href)
    {
        if (string.IsNullOrEmpty(href))
        {
            return;
        }

        // Only handle link clicks when the manifest declares the "input" capability
        if (Manifest is null || !Manifest.Capabilities.Contains("input"))
        {
            return;
        }

        var resolveResult = _viewModel.ResolveLinkTarget(href);

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
            OpenSystemBrowser(_commandService, href);
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
        var errorTitle = _stringLocalizer.GetString("Extension_LinkError_Title");
        var errorMessage = _stringLocalizer.GetString("Extension_LinkError_Message", href);
        await _dialogService.ShowAlertDialogAsync(errorTitle, errorMessage);
    }

    /// <summary>
    /// Loads localization strings using extension-owned localization files when available,
    /// falling back to the app's Resources.resw with the Ext_{Name}_ prefix.
    /// </summary>
    private Dictionary<string, string> LoadLocalizationStrings()
    {
        if (Manifest is not null && !string.IsNullOrEmpty(Manifest.Localization))
        {
            return ExtensionLocalizationHelper.LoadStrings(
                Manifest.ExtensionDirectory,
                Manifest.Localization);
        }

        // Fall back to app-level localization for extensions without their own localization files
        var prefix = $"Ext_{Manifest?.Name?.Replace(" ", "")}_";
        return WebViewLocalizationHelper.GetLocalizedStrings(_stringLocalizer, prefix);
    }

    public override async Task<Result> LoadContent()
    {
        _viewModel.InitializeFileTracking();
        await Task.CompletedTask;
        return Result.Ok();
    }

    public override async Task PrepareToClose()
    {
        _messengerService.UnregisterAll(this);

        Loaded -= ExtensionDocumentView_Loaded;

        _viewModel.ReloadRequested -= ViewModel_ReloadRequested;

        _viewModel.Cleanup();

        await base.PrepareToClose();
    }

    private async void ViewModel_ReloadRequested(object? sender, EventArgs e)
    {
        if (Host is not null)
        {
            await Host.NotifyExternalChangeAsync();
        }
    }
}
