using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Screenplay.Services;
using Celbridge.Screenplay.ViewModels;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Screenplay.Views;

public sealed partial class SceneDocumentView : WebView2DocumentView
{
    private readonly ILogger<SceneDocumentView> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IUserInterfaceService _userInterfaceService;

    private IResourceRegistry ResourceRegistry => _workspaceWrapper.WorkspaceService.ResourceService.Registry;

    public SceneDocumentViewModel ViewModel { get; }

    protected override ResourceKey FileResource => ViewModel.FileResource;

    private WebViewBridge? _bridge;
    private WebView2MessageChannel? _messageChannel;

    public SceneDocumentView(
        IServiceProvider serviceProvider,
        ILogger<SceneDocumentView> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        IUserInterfaceService userInterfaceService)
        : base(messengerService)
    {
        this.InitializeComponent();

        _logger = logger;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _userInterfaceService = userInterfaceService;

        ViewModel = serviceProvider.GetRequiredService<SceneDocumentViewModel>();

        WebView = SceneWebView;

        Loaded += SceneDocumentView_Loaded;

        _messengerService.Register<SceneContentUpdatedMessage>(this, OnSceneContentUpdated);
        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChanged);
    }

    public override async Task<Result> SetFileResource(ResourceKey fileResource)
    {
        var filePath = ResourceRegistry.GetResourcePath(fileResource);

        if (ResourceRegistry.GetResource(fileResource).IsFailure)
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
        var loadResult = ViewModel.LoadContent();
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load scene content")
                .WithErrors(loadResult);
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    private void OnSceneContentUpdated(object recipient, SceneContentUpdatedMessage message)
    {
        if (message.SceneResource != ViewModel.FileResource)
        {
            return;
        }

        var loadResult = ViewModel.LoadContent();
        if (loadResult.IsFailure)
        {
            _logger.LogError($"Failed to reload scene content: {loadResult}");
            return;
        }

        // Notify JS to reload content
        _bridge?.Document.NotifyExternalChange();
    }

    private void OnThemeChanged(object recipient, ThemeChangedMessage message)
    {
        if (_bridge is null)
        {
            return;
        }

        var isDark = message.Theme == UserInterfaceTheme.Dark;
        var themeName = isDark ? "Dark" : "Light";
        var themeInfo = new ThemeInfo(themeName, isDark);
        _bridge.Theme.NotifyChanged(themeInfo);
    }

    private async void SceneDocumentView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= SceneDocumentView_Loaded;

        await InitSceneViewAsync();
    }

    private async Task InitSceneViewAsync()
    {
        try
        {
            Guard.IsNotNull(WebView);

            await WebView.EnsureCoreWebView2Async();

            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "screenplay.celbridge",
                "Celbridge.Screenplay/Web/Screenplay",
                CoreWebView2HostResourceAccessKind.Allow);

            WebView2Helper.MapSharedAssets(WebView.CoreWebView2);

            WebView.CoreWebView2.Settings.IsWebMessageEnabled = true;

            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

            // Initialize the bridge BEFORE navigation
            _messageChannel = new WebView2MessageChannel(WebView.CoreWebView2);
            _bridge = new WebViewBridge(_messageChannel);

            // Register bridge handlers
            _bridge.OnInitialize(HandleInitializeAsync);
            _bridge.Document.OnLoad(HandleDocumentLoadAsync);

            // Navigate to the editor
            WebView.CoreWebView2.Navigate("https://screenplay.celbridge/index.html");

            // Initialize base WebView2 functionality (keyboard shortcuts, focus handling)
            await InitializeWebViewAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Screenplay Web View.");
        }
    }

    private Task<InitializeResult> HandleInitializeAsync(InitializeParams request)
    {
        // Validate protocol version
        if (request.ProtocolVersion != "1.0")
        {
            throw new BridgeException(
                JsonRpcErrorCodes.InvalidVersion,
                $"Unsupported protocol version: {request.ProtocolVersion}. Expected: 1.0");
        }

        // Build metadata
        var metadata = new DocumentMetadata(
            ViewModel.FilePath,
            ViewModel.FileResource.ToString(),
            Path.GetFileName(ViewModel.FilePath));

        // No localization strings needed for screenplay viewer
        var localization = new Dictionary<string, string>();

        // Build theme info
        var isDark = _userInterfaceService.UserInterfaceTheme == UserInterfaceTheme.Dark;
        var themeName = isDark ? "Dark" : "Light";
        var theme = new ThemeInfo(themeName, isDark);

        // Content is the generated HTML body content
        return Task.FromResult(new InitializeResult(ViewModel.HtmlContent, metadata, localization, theme));
    }

    private Task<LoadResult> HandleDocumentLoadAsync(LoadParams request)
    {
        // Reload content from the ViewModel
        var loadResult = ViewModel.LoadContent();
        if (loadResult.IsFailure)
        {
            throw new BridgeException(
                JsonRpcErrorCodes.InternalError,
                $"Failed to load scene content: {loadResult}");
        }

        DocumentMetadata? metadata = null;
        if (request.IncludeMetadata)
        {
            metadata = new DocumentMetadata(
                ViewModel.FilePath,
                ViewModel.FileResource.ToString(),
                Path.GetFileName(ViewModel.FilePath));
        }

        return Task.FromResult(new LoadResult(ViewModel.HtmlContent, metadata));
    }

    public override async Task PrepareToClose()
    {
        _messengerService.Unregister<SceneContentUpdatedMessage>(this);
        _messengerService.Unregister<ThemeChangedMessage>(this);

        _bridge?.Dispose();
        _messageChannel?.Detach();

        await base.PrepareToClose();
    }
}
