using System.Text.Json.Nodes;
using Celbridge.Broker;
using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.WebView;
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
    private readonly IProjectFileServer _projectFileServer;
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

    public bool IsUrlValid => ValidateAndNormalizeUrl(SourceUrl, Resource, out _);

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
        IProjectFileServer projectFileServer,
        IWebViewService webViewService)
    {
        _logger = logger;
        _stringLocalizer = stringLocalizer;
        _messengerService = messengerService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _projectFileServer = projectFileServer;
        _webViewService = webViewService;

        _messengerService.Register<WebAppNavigationStateChangedMessage>(this, OnWebAppNavigationStateChanged);
        PropertyChanged += ViewModel_PropertyChanged;
    }

    private void OnWebAppNavigationStateChanged(object recipient, WebAppNavigationStateChangedMessage message)
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
        if (!ValidateAndNormalizeUrl(SourceUrl, Resource, out var navigateUrl))
        {
            return;
        }

        if (string.Equals(navigateUrl, CurrentUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _messengerService.Send(new WebAppNavigateMessage(Resource, navigateUrl));
    }

    public IRelayCommand RefreshCommand => new RelayCommand(Refresh_Executed);
    private void Refresh_Executed()
    {
        _messengerService.Send(new WebAppRefreshMessage(Resource));
    }

    public IRelayCommand GoBackCommand => new RelayCommand(GoBack_Executed);
    private void GoBack_Executed()
    {
        _messengerService.Send(new WebAppGoBackMessage(Resource));
    }

    public IRelayCommand GoForwardCommand => new RelayCommand(GoForward_Executed);
    private void GoForward_Executed()
    {
        _messengerService.Send(new WebAppGoForwardMessage(Resource));
    }

    private bool ValidateAndNormalizeUrl(string url, ResourceKey contextResource, out string navigateUrl)
    {
        navigateUrl = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var trimmedUrl = url.Trim();
        var urlKind = _webViewService.ClassifyUrl(trimmedUrl);

        switch (urlKind)
        {
            case UrlType.WebUrl:
                if (Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    navigateUrl = trimmedUrl;
                    return true;
                }
                return false;

            case UrlType.LocalAbsolute:
                var resourcePath = _webViewService.StripLocalScheme(trimmedUrl);
                var absoluteUrl = _projectFileServer.ResolveProjectFileUrl(resourcePath, contextResource);
                if (!string.IsNullOrEmpty(absoluteUrl))
                {
                    navigateUrl = absoluteUrl;
                    return true;
                }
                // Fall back to resource registry check in case the file
                // server is not ready yet (e.g. during initial load).
                return ResolveResourceKey(resourcePath, contextResource);

            case UrlType.LocalPath:
                var relativeUrl = _projectFileServer.ResolveProjectFileUrl(trimmedUrl, contextResource);
                if (!string.IsNullOrEmpty(relativeUrl))
                {
                    navigateUrl = relativeUrl;
                    return true;
                }
                return ResolveResourceKey(trimmedUrl, contextResource);

            default:
                return false;
        }
    }

    /// <summary>
    /// Checks whether a path resolves to an existing resource, trying
    /// relative to the context resource's folder first, then as an
    /// absolute resource key.
    /// </summary>
    private bool ResolveResourceKey(string path, ResourceKey contextResource)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!contextResource.IsEmpty)
        {
            var contextFolder = contextResource.GetParent();
            var candidateKey = contextFolder.Combine(path);
            var getResult = _resourceRegistry.GetResource(candidateKey);
            if (getResult.IsSuccess)
            {
                return true;
            }
        }

        ResourceKey absoluteKey = path;
        var absoluteResult = _resourceRegistry.GetResource(absoluteKey);
        return absoluteResult.IsSuccess;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Resource))
        {
            var webFilePath = _resourceRegistry.GetResourcePath(Resource);
            var loadResult = LoadWebApp(webFilePath);
            if (loadResult.IsFailure)
            {
                _logger.LogError(loadResult, $"Failed to load .webapp file: {webFilePath}");
                return;
            }

            _suppressSaving = true;
            SourceUrl = loadResult.Value;
            _suppressSaving = false;
        }
        else if (e.PropertyName == nameof(SourceUrl) && !_suppressSaving)
        {
            var webFilePath = _resourceRegistry.GetResourcePath(Resource);
            var saveResult = SaveWebApp(webFilePath, SourceUrl);
            if (saveResult.IsFailure)
            {
                _logger.LogError(saveResult, $"Failed to save .webapp file: {webFilePath}");
                return;
            }
        }
    }

    private Result<string> LoadWebApp(string webFilePath)
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
            return Result<string>.Fail($"An exception occurred when loading .webapp file: {webFilePath}")
                .WithException(ex);
        }
    }

    private Result SaveWebApp(string webFilePath, string sourceUrl)
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
            return Result.Fail($"An exception occurred when saving .webapp file: {webFilePath}")
                .WithException(ex);
        }
    }
}
