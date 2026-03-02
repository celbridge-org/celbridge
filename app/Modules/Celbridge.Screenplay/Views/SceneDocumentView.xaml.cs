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

    private CelbridgeHost? _host;
    private HostChannel? _messageChannel;

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
        _host?.Document.NotifyExternalChange();
    }

    private void OnThemeChanged(object recipient, ThemeChangedMessage message)
    {
        // Theme change is detected by JavaScript via matchMedia
        // WebView2's PreferredColorScheme triggers the matchMedia change event
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

            // Set initial theme before navigation
            ApplyThemeToWebView();

            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "screenplay.celbridge",
                "Celbridge.Screenplay/Web/Screenplay",
                CoreWebView2HostResourceAccessKind.Allow);

            WebView2Helper.MapSharedAssets(WebView.CoreWebView2);

            WebView.CoreWebView2.Settings.IsWebMessageEnabled = true;

            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

            // Initialize the host BEFORE navigation
            _messageChannel = new HostChannel(WebView.CoreWebView2);
            _host = new CelbridgeHost(_messageChannel);

            // Register host handlers
            _host.OnInitialize(HandleInitializeAsync);
            _host.Document.OnLoad(HandleDocumentLoadAsync);

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
            throw new HostRpcException(
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

        // Content is the generated HTML body content
        return Task.FromResult(new InitializeResult(ViewModel.HtmlContent, metadata, localization));
    }

    private Task<LoadResult> HandleDocumentLoadAsync(LoadParams request)
    {
        // Reload content from the ViewModel
        var loadResult = ViewModel.LoadContent();
        if (loadResult.IsFailure)
        {
            throw new HostRpcException(
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

        _host?.Dispose();
        _messageChannel?.Detach();

        await base.PrepareToClose();
    }
}
