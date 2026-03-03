using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Screenplay.Services;
using Celbridge.Screenplay.ViewModels;
using Celbridge.Host;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Microsoft.Web.WebView2.Core;
using StreamJsonRpc;

namespace Celbridge.Screenplay.Views;

public sealed partial class SceneDocumentView : WebView2DocumentView, IHostDocument
{
    private readonly ILogger<SceneDocumentView> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IUserInterfaceService _userInterfaceService;

    private IResourceRegistry ResourceRegistry => _workspaceWrapper.WorkspaceService.ResourceService.Registry;

    public SceneDocumentViewModel ViewModel { get; }

    protected override ResourceKey FileResource => ViewModel.FileResource;

    private JsonRpc? _rpc;
    private HostRpcHandler? _rpcHandler;
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
        _rpc?.NotifyExternalChangeAsync();
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

            // Initialize StreamJsonRpc with this view as the handler
            _messageChannel = new HostChannel(WebView.CoreWebView2);
            _rpcHandler = new HostRpcHandler(_messageChannel);
            _rpc = new JsonRpc(_rpcHandler);

            // Ensure RPC method handlers run on the UI thread
            _rpc.SynchronizationContext = SynchronizationContext.Current;

            // Register this view as the handler for RPC interface
            _rpc.AddLocalRpcTarget<IHostDocument>(this, null);

            _rpc.StartListening();

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

    private DocumentMetadata CreateMetadata()
    {
        return new DocumentMetadata(
            ViewModel.FilePath,
            ViewModel.FileResource.ToString(),
            Path.GetFileName(ViewModel.FilePath));
    }

    #region IHostDocument

    public Task<InitializeResult> InitializeAsync(string protocolVersion)
    {
        // Validate protocol version
        if (protocolVersion != "1.0")
        {
            throw new HostRpcException(
                JsonRpcErrorCodes.InvalidVersion,
                $"Unsupported protocol version: {protocolVersion}. Expected: 1.0");
        }

        // Build metadata
        var metadata = CreateMetadata();

        // No localization strings needed for screenplay viewer
        var localization = new Dictionary<string, string>();

        // Content is the generated HTML body content
        return Task.FromResult(new InitializeResult(ViewModel.HtmlContent, metadata, localization));
    }

    public Task<LoadResult> LoadAsync()
    {
        // Reload content from the ViewModel
        var loadResult = ViewModel.LoadContent();
        if (loadResult.IsFailure)
        {
            throw new HostRpcException(
                JsonRpcErrorCodes.InternalError,
                $"Failed to load scene content: {loadResult}");
        }

        var metadata = CreateMetadata();

        return Task.FromResult(new LoadResult(ViewModel.HtmlContent, metadata));
    }

    public Task<SaveResult> SaveAsync(string content)
    {
        throw new NotSupportedException("Save is not supported by the Screenplay viewer (read-only).");
    }

    #endregion

    public override async Task PrepareToClose()
    {
        _messengerService.Unregister<SceneContentUpdatedMessage>(this);
        _messengerService.Unregister<ThemeChangedMessage>(this);

        // Dispose RPC and detach the message channel
        _rpc?.Dispose();
        _rpcHandler?.Dispose();
        _messageChannel?.Detach();
        _rpc = null;
        _rpcHandler = null;
        _messageChannel = null;

        await base.PrepareToClose();
    }
}
