using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.WebHost;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;

namespace Celbridge.Inspector.ViewModels;

public partial class WebInspectorViewModel : InspectorViewModel
{
    private const string SourceUrlFieldName = "source_url";

    private readonly ILogger<WebInspectorViewModel> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IWebViewService _webViewService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUrlValid))]
    [NotifyPropertyChangedFor(nameof(HasUrlError))]
    [NotifyPropertyChangedFor(nameof(UrlErrorMessage))]
    private string _sourceUrl = string.Empty;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [ObservableProperty]
    private bool _canRefresh;

    [ObservableProperty]
    private string _currentUrl = string.Empty;

    public bool IsUrlValid => ValidateAndNormalizeUrl(SourceUrl, out _);

    public bool HasUrlError => !string.IsNullOrWhiteSpace(SourceUrl) && !IsUrlValid;

    public string UrlErrorMessage
    {
        get
        {
            if (!HasUrlError)
            {
                return string.Empty;
            }

            return _stringLocalizer.GetString("WebInspector_InvalidUrl");
        }
    }

    private bool _suppressSaving;

    // Code gen requires a parameterless constructor
    public WebInspectorViewModel()
    {
        throw new NotImplementedException();
    }

    public WebInspectorViewModel(
        ILogger<WebInspectorViewModel> logger,
        IStringLocalizer stringLocalizer,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        IWebViewService webViewService)
    {
        _logger = logger;
        _stringLocalizer = stringLocalizer;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _webViewService = webViewService;

        _messengerService.Register<WebViewNavigationStateChangedMessage>(this, OnWebViewNavigationStateChanged);
        PropertyChanged += ViewModel_PropertyChanged;
    }

    private void OnWebViewNavigationStateChanged(object recipient, WebViewNavigationStateChangedMessage message)
    {
        if (message.DocumentResource == Resource)
        {
            CanGoBack = message.CanGoBack;
            CanGoForward = message.CanGoForward;
            CanRefresh = message.CanRefresh;
            CurrentUrl = message.CurrentUrl;
        }
    }

    public IRelayCommand HomeCommand => new RelayCommand(Home_Executed);
    private void Home_Executed()
    {
        if (!ValidateAndNormalizeUrl(SourceUrl, out var navigateUrl))
        {
            return;
        }

        if (string.Equals(navigateUrl, CurrentUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _messengerService.Send(new WebViewNavigateMessage(Resource, navigateUrl));
    }

    public IRelayCommand RefreshCommand => new RelayCommand(Refresh_Executed);
    private void Refresh_Executed()
    {
        _messengerService.Send(new WebViewRefreshMessage(Resource));
    }

    public IRelayCommand GoBackCommand => new RelayCommand(GoBack_Executed);
    private void GoBack_Executed()
    {
        _messengerService.Send(new WebViewGoBackMessage(Resource));
    }

    public IRelayCommand GoForwardCommand => new RelayCommand(GoForward_Executed);
    private void GoForward_Executed()
    {
        _messengerService.Send(new WebViewGoForwardMessage(Resource));
    }

    private bool ValidateAndNormalizeUrl(string url, out string navigateUrl)
    {
        navigateUrl = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var trimmedUrl = url.Trim();

        if (!_webViewService.IsExternalUrl(trimmedUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        navigateUrl = trimmedUrl;
        return true;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Resource))
        {
            _ = LoadWebViewAsync(Resource);
        }
        else if (e.PropertyName == nameof(SourceUrl) && !_suppressSaving)
        {
            _ = SaveWebViewAsync(Resource, SourceUrl);
        }
    }

    private async Task LoadWebViewAsync(ResourceKey resource)
    {
        // The .webview.cel file is a standalone .cel form, so SidecarService
        // treats the resource itself as the storage. Parse and gateway IO
        // live in the sidecar service; this method just plucks 'source_url'
        // from the frontmatter and posts it back to the inspector field.
        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        var readResult = await sidecarService.ReadAsync(resource);
        if (readResult.IsFailure)
        {
            _logger.LogError(readResult, $"Failed to read .webview.cel file: '{resource}'");
            return;
        }
        var read = readResult.Value;

        if (read.Outcome == SidecarReadOutcome.Broken)
        {
            _logger.LogError($"Failed to parse .webview.cel file '{resource}': {read.FailureMessage ?? "parse failed"}");
            return;
        }

        var sourceUrl = string.Empty;
        if (read.Outcome == SidecarReadOutcome.Healthy
            && read.Content is not null
            && read.Content.Frontmatter.TryGetValue(SourceUrlFieldName, out var urlObject))
        {
            if (urlObject is string urlValue)
            {
                sourceUrl = urlValue;
            }
            else
            {
                var actualType = urlObject?.GetType().Name ?? "null";
                _logger.LogWarning($"Field '{SourceUrlFieldName}' in '{resource}' is not a string (got {actualType})");
            }
        }

        _suppressSaving = true;
        SourceUrl = sourceUrl;
        _suppressSaving = false;
    }

    private async Task SaveWebViewAsync(ResourceKey resource, string sourceUrl)
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        var setResult = await sidecarService.SetFieldAsync(resource, SourceUrlFieldName, sourceUrl);
        if (setResult.IsFailure)
        {
            _logger.LogError(setResult, $"Failed to save .webview.cel file: '{resource}'");
        }
    }
}
