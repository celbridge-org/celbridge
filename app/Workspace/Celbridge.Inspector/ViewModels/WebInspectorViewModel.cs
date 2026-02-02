using System.Text.Json.Nodes;
using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.Inspector.ViewModels;

public partial class WebInspectorViewModel : InspectorViewModel
{
    private readonly ILogger<WebInspectorViewModel> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IResourceRegistry _resourceRegistry;

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

    public bool IsUrlValid => ValidateAndNormalizeUrl(SourceUrl, out _);

    public bool HasUrlError => !string.IsNullOrWhiteSpace(SourceUrl) && !IsUrlValid;

    public string UrlErrorMessage => HasUrlError ? "The entered URL is not valid. Please enter a valid web address." : string.Empty;

    private bool _supressSaving;

    // Code gen requires a parameterless constructor
    public WebInspectorViewModel()
    {
        throw new NotImplementedException();
    }

    public WebInspectorViewModel(
        ILogger<WebInspectorViewModel> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

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
        }
    }

    public IRelayCommand NavigateCommand => new RelayCommand(Navigate_Executed);
    private void Navigate_Executed()
    {
        if (!ValidateAndNormalizeUrl(SourceUrl, out var normalizedUrl))
        {
            return;
        }

        // Update the source URL if it was normalized (e.g., added https://)
        if (normalizedUrl != SourceUrl)
        {
            SourceUrl = normalizedUrl;
        }

        _messengerService.Send(new WebAppNavigateMessage(Resource, normalizedUrl));
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

    private static bool ValidateAndNormalizeUrl(string url, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var trimmedUrl = url.Trim();

        // If it already has a supported scheme, validate it as-is
        if (trimmedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmedUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFile))
            {
                normalizedUrl = trimmedUrl;
                return true;
            }
            return false;
        }

        // Try adding https:// and validate using .NET's Uri class
        // This handles IDN (internationalized domain names), IPv4, IPv6, ports, paths, etc.
        var urlWithScheme = $"https://{trimmedUrl}";
        if (Uri.TryCreate(urlWithScheme, UriKind.Absolute, out var testUri) &&
            testUri.Scheme == Uri.UriSchemeHttps)
        {
            // Additional validation: ensure the host is a valid DNS name, IPv4, or IPv6
            var hostType = Uri.CheckHostName(testUri.Host);
            if (hostType == UriHostNameType.Dns ||
                hostType == UriHostNameType.IPv4 ||
                hostType == UriHostNameType.IPv6)
            {
                normalizedUrl = urlWithScheme;
                return true;
            }
        }

        return false;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Resource))
        {
            var webFilePath = _resourceRegistry.GetResourcePath(Resource);
            var loadResult = LoadURL(webFilePath);
            if (loadResult.IsFailure)
            {
                _logger.LogError(loadResult, $"Failed to load URL from file: {webFilePath}");
                return;
            }

            _supressSaving = true;
            SourceUrl = loadResult.Value;
            _supressSaving = false;
        }
        else if (e.PropertyName == nameof(SourceUrl) && !_supressSaving)
        {
            var webFilePath = _resourceRegistry.GetResourcePath(Resource);
            var saveResult = SaveURL(webFilePath, SourceUrl);
            if (saveResult.IsFailure)
            {
                _logger.LogError(saveResult, $"Failed to save URL to file: {webFilePath}");
                return;
            }
        }
    }

    private Result<string> LoadURL(string webFilePath)
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
                // No data populated yet in the .webapp file, default to empty url
                return Result<string>.Ok(string.Empty);
            }

            var jsonObject = JsonNode.Parse(json) as JsonObject;
            if (jsonObject is null)
            {
                return Result<string>.Fail($"Failed to parse JSON file: {webFilePath}");
            }

            var urlToken = jsonObject["sourceUrl"];
            if (urlToken is null)
            {
                return Result<string>.Fail($"'sourceUrl' property not found in JSON file: {webFilePath}");
            }

            string url = urlToken.ToString();

            return Result<string>.Ok(url);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"An exception occurred when loading URL from file: {webFilePath}")
                .WithException(ex);
        }
    }

    private Result SaveURL(string webFilePath, string url)
    {
        try
        {
            // Create a new JSON object with just the 'url' property
            var jsonObject = new JsonObject
            {
                ["sourceUrl"] = url
            };

            // Write the new JSON object to the file, overwriting any existing content
            File.WriteAllText(webFilePath, jsonObject.ToJsonString());

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when saving URL to file: {webFilePath}")
                .WithException(ex);
        }
    }
}
