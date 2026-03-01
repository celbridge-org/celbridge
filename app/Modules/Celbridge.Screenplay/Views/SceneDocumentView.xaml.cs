using Celbridge.Documents.Views;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Screenplay.Services;
using Celbridge.Screenplay.ViewModels;
using Celbridge.UserInterface;
using Celbridge.Workspace;

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
        var isDarkMode = _userInterfaceService.UserInterfaceTheme == UserInterfaceTheme.Dark;
        var loadResult = ViewModel.LoadContent(isDarkMode);
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load scene content")
                .WithErrors(loadResult);
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    private async void OnSceneContentUpdated(object recipient, SceneContentUpdatedMessage message)
    {
        if (message.SceneResource != ViewModel.FileResource)
        {
            return;
        }

        var isDarkMode = _userInterfaceService.UserInterfaceTheme == UserInterfaceTheme.Dark;
        var loadResult = ViewModel.LoadContent(isDarkMode);
        if (loadResult.IsFailure)
        {
            _logger.LogError($"Failed to reload scene content: {loadResult}");
            return;
        }

        await NavigateToHtmlContent();
    }

    private async void OnThemeChanged(object recipient, ThemeChangedMessage message)
    {
        var isDarkMode = message.Theme == UserInterfaceTheme.Dark;
        var loadResult = ViewModel.LoadContent(isDarkMode);
        if (loadResult.IsFailure)
        {
            _logger.LogError($"Failed to reload scene content after theme change: {loadResult}");
            return;
        }

        await NavigateToHtmlContent();
    }

    private async void SceneDocumentView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= SceneDocumentView_Loaded;

        await InitSceneViewAsync();
    }

    private async Task InitSceneViewAsync()
    {
        await InitializeWebViewAsync();

        Guard.IsNotNull(WebView);

        var isDarkMode = _userInterfaceService.UserInterfaceTheme == UserInterfaceTheme.Dark;
        var loadResult = ViewModel.LoadContent(isDarkMode);
        if (loadResult.IsFailure)
        {
            _logger.LogError($"Failed to load scene content: {loadResult}");
            return;
        }

        await NavigateToHtmlContent();
    }

    private async Task NavigateToHtmlContent()
    {
        if (WebView is null)
        {
            return;
        }

        try
        {
            await WebView.EnsureCoreWebView2Async();
            WebView.CoreWebView2.NavigateToString(ViewModel.HtmlContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to HTML content");
        }
    }

    public override async Task PrepareToClose()
    {
        _messengerService.Unregister<SceneContentUpdatedMessage>(this);
        _messengerService.Unregister<ThemeChangedMessage>(this);

        await base.PrepareToClose();
    }
}
