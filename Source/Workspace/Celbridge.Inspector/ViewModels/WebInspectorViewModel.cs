using System.Text.Json.Nodes;
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
    private readonly ILogger<WebInspectorViewModel> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IMessengerService _messengerService;
    private readonly IResourceRegistry _resourceRegistry;
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
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
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
            var resolveLoadResult = _resourceRegistry.ResolveResourcePath(Resource);
            if (resolveLoadResult.IsFailure)
            {
                _logger.LogError(resolveLoadResult, $"Failed to resolve path for resource: '{Resource}'");
                return;
            }
            var loadResult = LoadWebView(resolveLoadResult.Value);
            if (loadResult.IsFailure)
            {
                _logger.LogError(loadResult, $"Failed to load .webview file: {resolveLoadResult.Value}");
                return;
            }

            _suppressSaving = true;
            SourceUrl = loadResult.Value;
            _suppressSaving = false;
        }
        else if (e.PropertyName == nameof(SourceUrl) && !_suppressSaving)
        {
            var resolveSaveResult = _resourceRegistry.ResolveResourcePath(Resource);
            if (resolveSaveResult.IsFailure)
            {
                _logger.LogError(resolveSaveResult, $"Failed to resolve path for resource: '{Resource}'");
                return;
            }
            var saveResult = SaveWebView(resolveSaveResult.Value, SourceUrl);
            if (saveResult.IsFailure)
            {
                _logger.LogError(saveResult, $"Failed to save .webview file: {resolveSaveResult.Value}");
                return;
            }
        }
    }

    private Result<string> LoadWebView(string webFilePath)
    {
        if (!File.Exists(webFilePath))
        {
            return Result<string>.Fail($"File not found at path: {webFilePath}");
        }

        try
        {
            var json = File.ReadAllText(webFilePath);

            if (string.IsNullOrEmpty(json))
            {
                return Result<string>.Ok(string.Empty);
            }

            var jsonObject = JsonNode.Parse(json) as JsonObject;
            if (jsonObject is null)
            {
                return Result<string>.Fail($"Failed to parse JSON file: {webFilePath}");
            }

            var sourceUrl = jsonObject["sourceUrl"]?.ToString() ?? string.Empty;

            return Result<string>.Ok(sourceUrl);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"An exception occurred when loading .webview file: {webFilePath}")
                .WithException(ex);
        }
    }

    private Result SaveWebView(string webFilePath, string sourceUrl)
    {
        try
        {
            var jsonObject = new JsonObject
            {
                ["sourceUrl"] = sourceUrl
            };

            File.WriteAllText(webFilePath, jsonObject.ToJsonString());

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when saving .webview file: {webFilePath}")
                .WithException(ex);
        }
    }
}
