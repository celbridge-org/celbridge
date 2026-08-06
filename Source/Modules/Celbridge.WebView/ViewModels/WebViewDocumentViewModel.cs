using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Server;
using Celbridge.WebHost;
using Celbridge.WebView.Services;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.WebView.ViewModels;

/// <summary>
/// The lifecycle stage of the URL bar's download indicator.
/// </summary>
public enum WebViewDownloadStatus
{
    None,
    InProgress,
    Succeeded,
    Failed,
}

public partial class WebViewDocumentViewModel : DocumentViewModel
{
    private readonly ICommandService _commandService;
    private readonly IWebViewService _webViewService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IServerService _serverService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeUrlValid))]
    [NotifyPropertyChangedFor(nameof(IsHomeUrlInvalid))]
    [NotifyPropertyChangedFor(nameof(HomeUrlTooltip))]
    [NotifyPropertyChangedFor(nameof(CanSetCurrentPageAsHome))]
    private string _sourceUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUrlBarVisible))]
    private bool _showUrlBar = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsPanelVisible))]
    private bool _isSettingsPanelOpen;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReload))]
    [NotifyPropertyChangedFor(nameof(IsReloadOrStopEnabled))]
    [NotifyPropertyChangedFor(nameof(CanOpenInBrowser))]
    [NotifyPropertyChangedFor(nameof(CanSetCurrentPageAsHome))]
    [NotifyPropertyChangedFor(nameof(CanCreateDocumentFromCurrentPage))]
    private string _currentUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReloadOrStopEnabled))]
    [NotifyPropertyChangedFor(nameof(IsReloadIconVisible))]
    private bool _isNavigating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadIndicatorVisible))]
    [NotifyPropertyChangedFor(nameof(IsDownloadInProgress))]
    [NotifyPropertyChangedFor(nameof(IsDownloadSucceeded))]
    [NotifyPropertyChangedFor(nameof(IsDownloadFailed))]
    private WebViewDownloadStatus _downloadStatus = WebViewDownloadStatus.None;

    /// <summary>
    /// The imported resource of the most recent completed download. Clicking the
    /// settled download indicator reveals it in the Explorer.
    /// </summary>
    public ResourceKey LastDownloadedResource { get; private set; } = ResourceKey.Empty;

    private WebViewDocumentRole _role;

    /// <summary>
    /// Selects how LoadContent and NavigateUrl interpret the backing resource. Set
    /// by the view before the first LoadContent call. Defaults to ExternalUrl, which
    /// matches the .webview document behaviour assumed by the parameterless code-gen flow.
    /// </summary>
    public WebViewDocumentRole Role
    {
        get => _role;
        set
        {
            _role = value;
            OnPropertyChanged(nameof(IsUrlBarVisible));
            OnPropertyChanged(nameof(IsSettingsPanelVisible));
        }
    }

    /// <summary>
    /// True when the browser-style URL bar should be shown: the external-URL role
    /// only, and only while the document does not hide it via show_url_bar.
    /// </summary>
    public bool IsUrlBarVisible => Role == WebViewDocumentRole.ExternalUrl && ShowUrlBar;

    /// <summary>
    /// True when the settings side panel should occupy its column. Like the URL bar,
    /// the panel is external-URL chrome and never appears for the HTML viewer.
    /// </summary>
    public bool IsSettingsPanelVisible => Role == WebViewDocumentRole.ExternalUrl && IsSettingsPanelOpen;

    /// <summary>
    /// True when the configured Home URL is a navigable external URL.
    /// </summary>
    public bool IsHomeUrlValid => TryNormalizeUserUrl(SourceUrl, out _);

    /// <summary>
    /// True when the user has entered a Home URL that cannot be navigated to. A blank
    /// Home URL is unconfigured rather than wrong, so it does not report as invalid.
    /// </summary>
    public bool IsHomeUrlInvalid => !string.IsNullOrWhiteSpace(SourceUrl) && !IsHomeUrlValid;

    /// <summary>
    /// The whole Home URL, for a hover over the address box that shows it. Null when there is no URL
    /// configured, so an empty box raises no tooltip at all.
    /// </summary>
    public string? HomeUrlTooltip
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SourceUrl))
            {
                return null;
            }

            return SourceUrl;
        }
    }

    /// <summary>
    /// True when the page on screen is somewhere other than the configured Home URL,
    /// so adopting it as the new Home URL would change something.
    /// </summary>
    public bool CanSetCurrentPageAsHome => IsPageUrl(CurrentUrl) && CurrentUrl != SourceUrl;

    /// <summary>
    /// True when the page on screen can be captured as a new .webview document.
    /// </summary>
    public bool CanCreateDocumentFromCurrentPage => IsPageUrl(CurrentUrl) && !FileResource.IsEmpty;

    public bool CanReload => IsPageUrl(CurrentUrl);

    public bool IsReloadOrStopEnabled => IsNavigating || CanReload;

    /// <summary>
    /// True when the reload/stop button shows the reload icon; while a navigation
    /// is in flight it shows the stop icon instead.
    /// </summary>
    public bool IsReloadIconVisible => !IsNavigating;

    public bool CanOpenInBrowser => IsPageUrl(CurrentUrl);

    public bool IsDownloadIndicatorVisible => DownloadStatus != WebViewDownloadStatus.None;

    public bool IsDownloadInProgress => DownloadStatus == WebViewDownloadStatus.InProgress;

    public bool IsDownloadSucceeded => DownloadStatus == WebViewDownloadStatus.Succeeded;

    public bool IsDownloadFailed => DownloadStatus == WebViewDownloadStatus.Failed;

    /// <summary>
    /// The URL the view should navigate to. For .webview documents this is the configured source URL
    /// verbatim. For the HTML viewer it is the loopback /project/ URL on the Skia heads, or the project
    /// virtual-host URL on Windows.
    /// </summary>
    public string NavigateUrl
    {
        get
        {
            if (Role == WebViewDocumentRole.HtmlViewer)
            {
                if (FileResource.IsEmpty)
                {
                    return string.Empty;
                }

                // URL path is the bare resource path. The "project:" prefix that
                // ResourceKey.ToString() emits is for serialised diagnostics,
                // not URL construction.

                // Served over the loopback file server's /project/ route. Relative asset
                // references in the HTML resolve against this origin.
                return $"http://127.0.0.1:{_serverService.Port}/project/{FileResource.Path}";
            }

            return SourceUrl;
        }
    }

    // Code gen requires a parameterless constructor
    public WebViewDocumentViewModel()
    {
        throw new NotImplementedException();
    }

    public WebViewDocumentViewModel(
        ICommandService commandService,
        IWebViewService webViewService,
        IWorkspaceWrapper workspaceWrapper,
        IServerService serverService)
    {
        _commandService = commandService;
        _webViewService = webViewService;
        _workspaceWrapper = workspaceWrapper;
        _serverService = serverService;
    }

    public async Task<Result> LoadContent()
    {
        if (Role == WebViewDocumentRole.HtmlViewer)
        {
            // HTML viewer content is served by the file server (loopback /project/ route, or the project
            // virtual host on Windows). Nothing to parse. Succeeding here lets TryNavigate run.
            await Task.CompletedTask;
            return Result.Ok();
        }

        // A reload after a rename re-enters here, so detach the change handler
        // while the parsed values are pushed onto the properties.
        PropertyChanged -= WebViewDocumentViewModel_PropertyChanged;

        var loadResult = await LoadDocumentSettingsAsync();
        if (loadResult.IsFailure)
        {
            return loadResult;
        }

        PropertyChanged += WebViewDocumentViewModel_PropertyChanged;

        return Result.Ok();
    }

    private async Task<Result> LoadDocumentSettingsAsync()
    {
        // The .webview file is a small TOML document that carries the configured
        // external URL and chrome settings. Read via the gateway so the load picks
        // up the same containment validation as writes.
        var resourceFileSystem = GetFileSystem();

        var infoResult = await resourceFileSystem.GetInfoAsync(FileResource);
        if (infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            // No file on disk yet (e.g. just created via the Add File dialog).
            // Treat as a blank URL so the view shows nothing rather than failing.
            SourceUrl = string.Empty;
            ShowUrlBar = true;
            return Result.Ok();
        }

        var readResult = await LoadTextFromFileAsync();
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read '{ExplorerConstants.WebViewExtension}' file '{FileResource}'")
                .WithErrors(readResult);
        }

        var parseResult = WebViewFileContent.TryParse(readResult.Value);
        if (parseResult.IsFailure)
        {
            return Result.Fail($"Failed to parse '{ExplorerConstants.WebViewExtension}' file '{FileResource}'")
                .WithErrors(parseResult);
        }
        var content = parseResult.Value;

        ShowUrlBar = content.ShowUrlBar;

        var sourceUrl = content.SourceUrl.Trim();
        if (string.IsNullOrEmpty(sourceUrl))
        {
            SourceUrl = string.Empty;
            return Result.Ok();
        }

        if (!_webViewService.IsExternalUrl(sourceUrl))
        {
            return Result.Fail(
                $"{ExplorerConstants.WebViewExtension} documents only support external http/https URLs. Configured URL: '{sourceUrl}'");
        }

        SourceUrl = sourceUrl;
        return Result.Ok();
    }

    public async Task<Result> SaveDocumentContent()
    {
        // Don't immediately try to save again if the save fails.
        HasUnsavedChanges = false;
        SaveTimer = 0;

        var content = new WebViewFileContent(SourceUrl, ShowUrlBar);
        return await SaveTextToFileAsync(content.ToToml());
    }

    /// <summary>
    /// Validates a user-entered address, prefixing https:// when no scheme was
    /// typed. Returns false when the input cannot become a navigable external URL.
    /// </summary>
    public bool TryNormalizeUserUrl(string input, out string url)
    {
        url = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = $"https://{trimmed}";
        }

        if (!_webViewService.IsExternalUrl(trimmed))
        {
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        url = trimmed;
        return true;
    }

    public void OpenBrowser(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        _commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = url;
        });
    }

    public void ToggleSettingsPanel()
    {
        IsSettingsPanelOpen = !IsSettingsPanelOpen;
    }

    /// <summary>
    /// Closes the settings panel. The panel needs a way out of its own, because a document that hides the
    /// URL bar hides the toggle that opened it.
    /// </summary>
    public void CloseSettingsPanel()
    {
        IsSettingsPanelOpen = false;
    }

    /// <summary>
    /// Adopts the page currently on screen as the document's Home URL.
    /// </summary>
    public void SetCurrentPageAsHome()
    {
        if (!TryNormalizeUserUrl(CurrentUrl, out var homeUrl))
        {
            return;
        }

        SourceUrl = homeUrl;
    }

    /// <summary>
    /// Opens the dialog that names a new .webview document for the page currently on screen. The document
    /// is created in this document's folder, so related links stay together.
    /// </summary>
    public void CreateDocumentFromCurrentPage()
    {
        if (!TryNormalizeUserUrl(CurrentUrl, out var pageUrl))
        {
            return;
        }

        _commandService.Execute<ICreateWebViewDialogCommand>(command =>
        {
            command.SourceUrl = pageUrl;
            command.DestFolderResource = FileResource.GetParent();
        });
    }

    public void BeginDownload()
    {
        DownloadStatus = WebViewDownloadStatus.InProgress;
    }

    public void CompleteDownload(ResourceKey importedResource)
    {
        LastDownloadedResource = importedResource;
        DownloadStatus = WebViewDownloadStatus.Succeeded;
    }

    public void FailDownload()
    {
        DownloadStatus = WebViewDownloadStatus.Failed;
    }

    public void ClearDownloadIndicator()
    {
        DownloadStatus = WebViewDownloadStatus.None;
    }

    /// <summary>
    /// Reveals the most recent completed download in the Explorer panel.
    /// </summary>
    public void RevealLastDownload()
    {
        if (LastDownloadedResource.IsEmpty)
        {
            return;
        }

        _commandService.Execute<ISelectResourceCommand>(command =>
        {
            command.Resource = LastDownloadedResource;
            command.ShowExplorerPanel = true;
        });
    }

    protected override IResourceFileSystem GetFileSystem()
    {
        // Route the base-class load and save helpers through the injected wrapper
        // so tests can substitute the file system without a service locator.
        return _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
    }

    private void WebViewDocumentViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SourceUrl) ||
            e.PropertyName == nameof(ShowUrlBar))
        {
            OnDataChanged();
        }
    }

    // A blank WebView reports an empty source or about:blank; neither is a page
    // the user can reload or open in the system browser.
    private static bool IsPageUrl(string url)
    {
        return !string.IsNullOrEmpty(url) &&
               url != "about:blank";
    }
}
