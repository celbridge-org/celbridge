using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Screenplay.Services;
using Celbridge.Screenplay.ViewModels;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.WebView;
using Microsoft.Web.WebView2.Core;
using StreamJsonRpc;

namespace Celbridge.Screenplay.Views;

public sealed partial class SceneDocumentView : WebViewDocumentView, IHostDocument
{
    private readonly ILogger<SceneDocumentView> _logger;
    private readonly IMessengerService _messengerService;

    public SceneDocumentViewModel ViewModel { get; }

    protected override DocumentViewModel DocumentViewModel => ViewModel;

    public SceneDocumentView(
        IServiceProvider serviceProvider,
        ILogger<SceneDocumentView> logger,
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService,
        IWebViewFactory webViewFactory,
        IFeatureFlags featureFlags)
        : base(messengerService, webViewFactory, featureFlags)
    {
        this.InitializeComponent();

        _logger = logger;
        _messengerService = messengerService;

        ViewModel = serviceProvider.GetRequiredService<SceneDocumentViewModel>();

        // Set the container where the WebView will be placed
        WebViewContainer = SceneWebViewContainer;

        Loaded += SceneDocumentView_Loaded;

        _messengerService.Register<SceneContentUpdatedMessage>(this, OnSceneContentUpdated);

        EnableThemeSyncing(userInterfaceService);
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

        // Route through the base orchestration for consistency with the other editors. Screenplay
        // has no editor state to preserve (read-only), so the save/restore round-trip is a no-op,
        // but using the same orchestration keeps the reload contract uniform across editors.
        _ = ReloadWithStatePreservationAsync();
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
            // Acquire WebView from factory and add to container
            await AcquireWebViewAsync();

            // Set initial theme before navigation
            ApplyThemeToWebView();

            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "screenplay.celbridge",
                "Celbridge.Screenplay/Web/Screenplay",
                CoreWebView2HostResourceAccessKind.Allow);

            // Initialize the host
            InitializeHost();

            if (Host is null)
            {
                _logger.LogError("Failed to initialize host");
                return;
            }

            // Register this view as the handler for additional RPC interfaces
            Host.AddLocalRpcTarget<IHostDocument>(this);

            StartHostListener();

            // Navigate to the editor
            WebView.CoreWebView2.Navigate("https://screenplay.celbridge/index.html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Screenplay Web View.");
        }
    }

    #region IHostDocument

        public Task<InitializeResult> InitializeAsync(string protocolVersion)
        {
            DocumentRpcMethods.ValidateProtocolVersion(protocolVersion);

            var metadata = CreateDocumentMetadata();

            // Content is the generated HTML body content
            return Task.FromResult(new InitializeResult(ViewModel.HtmlContent, metadata));
        }

    public Task<LoadResult> LoadAsync()
    {
        // Reload content from the ViewModel
        var loadResult = ViewModel.LoadContent();
        if (loadResult.IsFailure)
        {
            throw new LocalRpcException($"Failed to load scene content: {loadResult}");
        }

        var metadata = CreateDocumentMetadata();

        return Task.FromResult(new LoadResult(ViewModel.HtmlContent, metadata));
    }

    public Task<SaveResult> SaveAsync(string content)
    {
        throw new NotSupportedException("Save is not supported by the Screenplay viewer (read-only).");
    }

    public void OnContentLoaded(ContentLoadedReason reason = ContentLoadedReason.Initial)
    {
        SetContentLoaded(reason);
    }

    #endregion

    public override async Task PrepareToClose()
    {
        _messengerService.UnregisterAll(this);

        Loaded -= SceneDocumentView_Loaded;

        ViewModel.Cleanup();

        await base.PrepareToClose();
    }
}
