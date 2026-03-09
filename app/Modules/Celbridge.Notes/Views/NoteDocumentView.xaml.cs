using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Host.Helpers;
using Celbridge.Logging;
using Celbridge.Notes.Services;
using Celbridge.Notes.ViewModels;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.WebView;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Microsoft.Web.WebView2.Core;
using StreamJsonRpc;

namespace Celbridge.Notes.Views;

public sealed partial class NoteDocumentView : WebView2DocumentView, IHostDocument, IHostDialog
{
    private readonly ILogger _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;

    private NoteHost? _noteHost;

    public NoteDocumentViewModel ViewModel { get; }

    public override ResourceKey FileResource => ViewModel.FileResource;

    public NoteDocumentView(
        IServiceProvider serviceProvider,
        ILogger<NoteDocumentView> logger,
        ICommandService commandService,
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService,
        IWorkspaceWrapper workspaceWrapper,
        IStringLocalizer stringLocalizer,
        IDialogService dialogService,
        IWebViewFactory webViewFactory)
        : base(messengerService, webViewFactory)
    {
        ViewModel = serviceProvider.GetRequiredService<NoteDocumentViewModel>();

        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;
        _userInterfaceService = userInterfaceService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;

        this.InitializeComponent();

        // Set the container where the WebView will be placed
        WebViewContainer = NoteWebViewContainer;

        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChanged);

        Loaded += NoteDocumentView_Loaded;

        ViewModel.ReloadRequested += ViewModel_ReloadRequested;
    }

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
    }

    public override async Task<Result> SaveDocument()
    {
        if (_noteHost is null)
        {
            _logger.LogDebug("Save skipped - NoteHost not initialized");
            return Result.Ok();
        }

        if (!TryBeginSave())
        {
            _logger.LogDebug("Save already in progress, queuing pending save");
            return Result.Ok();
        }

        // Request the JS side to save - it will call document/save
        // which triggers our HandleSaveDocumentAsync handler
        await _noteHost.NotifyRequestSaveAsync();

        return await ViewModel.SaveDocument();
    }

    private async void NoteDocumentView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= NoteDocumentView_Loaded;

        await InitNoteViewAsync();
    }

    private async Task InitNoteViewAsync()
    {
        try
        {
            // Acquire WebView from factory and add to container
            await AcquireWebViewAsync();

            // Set up virtual host mapping for Note editor assets
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "note.celbridge",
                "Celbridge.Notes/Web/note",
                CoreWebView2HostResourceAccessKind.Allow);

            // Map the project folder so resource key image paths resolve correctly
            var projectFolder = _resourceRegistry.ProjectFolderPath;
            if (!string.IsNullOrEmpty(projectFolder))
            {
                WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "project.celbridge",
                    projectFolder,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            // Sync WebView2 color scheme with the app theme
            ApplyThemeToWebView();

            var settings = WebView.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = true;
            settings.AreDefaultContextMenusEnabled = true;

            // Cancel all navigations except the initial editor page load
            WebView.NavigationStarting += (s, args) =>
            {
                var uri = args.Uri;
                if (string.IsNullOrEmpty(uri))
                {
                    return;
                }

                if (uri.StartsWith("https://note.celbridge/index.html"))
                {
                    return;
                }

                args.Cancel = true;
            };

            // Block all new window requests
            WebView.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                args.Handled = true;
            };

            // Initialize the host
            InitializeHost();

            if (Host is null)
            {
                _logger.LogError("Failed to initialize host");
                return;
            }

            // Create the Note-specific host wrapper
            _noteHost = new NoteHost(Host);

            // Register this view as the handler for additional RPC interfaces
            _noteHost.AddLocalRpcTarget<IHostDocument>(this);
            _noteHost.AddLocalRpcTarget<IHostDialog>(this);

            _noteHost.StartListening();

            // Navigate to the editor
            WebView.CoreWebView2.Navigate("https://note.celbridge/index.html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Note Web View.");
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

    public async Task<InitializeResult> InitializeAsync(string protocolVersion)
    {
        // Validate protocol version
        if (protocolVersion != "1.0")
        {
            throw new LocalRpcException($"Unsupported protocol version: {protocolVersion}. Expected: 1.0");
        }

        // Load content from file
        var content = await ViewModel.LoadNoteContent();

        var metadata = CreateMetadata();

        // Gather localization strings
        var localization = WebViewLocalizationHelper.GetLocalizedStrings(_stringLocalizer, "Note_");

        return new InitializeResult(content, metadata, localization);
    }

    public async Task<SaveResult> SaveAsync(string content)
    {
        try
        {
            var saveResult = await ViewModel.SaveNoteToFile(content);

            if (saveResult.IsFailure)
            {
                _logger.LogError(saveResult, "Failed to save note data");
                CompleteSave();
                return new SaveResult(false, saveResult.Error);
            }

            // Check if there's a pending save that needs processing
            if (CompleteSave())
            {
                _logger.LogDebug("Processing pending save request");
                ViewModel.OnDataChanged();
            }

            return new SaveResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during save");
            CompleteSave();
            return new SaveResult(false, ex.Message);
        }
    }

    public async Task<LoadResult> LoadAsync()
    {
        var content = await ViewModel.LoadNoteContent();
        var metadata = CreateMetadata();

        return new LoadResult(content, metadata);
    }

    #endregion

    #region IHostDialog

    public async Task<PickImageResult> PickImageAsync(IReadOnlyList<string>? extensions = null)
    {
        var extensionsArray = extensions?.ToArray();
        if (extensionsArray is null || extensionsArray.Length == 0)
        {
            extensionsArray =
            [
                ".png",
                ".jpg",
                ".jpeg",
                ".gif",
                ".webp",
                ".svg",
                ".bmp"
            ];
        }

        var title = _stringLocalizer.GetString("Note_SelectImage_Title");
        var result = await _dialogService.ShowResourcePickerDialogAsync(extensionsArray, title, showPreview: true);

        if (result.IsSuccess)
        {
            var resourceKey = result.Value.ToString();
            var relativePath = GetRelativePathFromResourceKey(resourceKey);
            return new PickImageResult(relativePath);
        }

        return new PickImageResult(null);
    }

    public async Task<PickFileResult> PickFileAsync(IReadOnlyList<string>? extensions = null)
    {
        var title = _stringLocalizer.GetString("Note_SelectFile_Title");
        var extensionsArray = extensions?.ToArray() ?? [];
        var result = await _dialogService.ShowResourcePickerDialogAsync(extensionsArray, title);

        if (result.IsSuccess)
        {
            var resourceKey = result.Value.ToString();
            var relativePath = GetRelativePathFromResourceKey(resourceKey);
            return new PickFileResult(relativePath);
        }

        return new PickFileResult(null);
    }

    public Task<AlertResult> AlertAsync(string title, string message)
    {
        throw new NotSupportedException("Alert dialog is not used by the Note editor.");
    }

    #endregion

    private void OpenSystemBrowser(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return;
        }

        _commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = uri;
        });
    }

    /// <summary>
    /// Normalizes a path by resolving '..' and '.' segments.
    /// This is used for both link and image relative path resolution.
    /// </summary>
    private static string NormalizeResourcePath(string path)
    {
        var segments = path.Split('/');
        var stack = new Stack<string>();
        foreach (var segment in segments)
        {
            if (segment == ".." && stack.Count > 0)
            {
                stack.Pop();
            }
            else if (segment != "." && !string.IsNullOrEmpty(segment))
            {
                stack.Push(segment);
            }
        }
        return string.Join("/", stack.Reverse());
    }

    /// <summary>
    /// Gets the base path (folder) of the current document for resolving relative paths.
    /// Returns an empty string if the document is at the project root.
    /// </summary>
    private string GetDocumentBasePath()
    {
        var fileResourcePath = ViewModel.FileResource.ToString();
        var directoryName = Path.GetDirectoryName(fileResourcePath);
        return directoryName?.Replace('\\', '/') ?? "";
    }

    /// <summary>
    /// Converts an absolute Resource Key to a path relative to the current document.
    /// Uses forward slashes only for consistency.
    /// </summary>
    private string GetRelativePathFromResourceKey(string resourceKey)
    {
        if (string.IsNullOrEmpty(resourceKey))
        {
            return string.Empty;
        }

        var documentBasePath = GetDocumentBasePath();
        if (string.IsNullOrEmpty(documentBasePath))
        {
            // Document is at project root, Resource Key is already relative
            return resourceKey;
        }

        var documentSegments = documentBasePath.Split('/');
        var targetSegments = resourceKey.Split('/');

        // Find common prefix length
        var commonLength = 0;
        var minLength = Math.Min(documentSegments.Length, targetSegments.Length);
        for (var i = 0; i < minLength; i++)
        {
            if (documentSegments[i] == targetSegments[i])
            {
                commonLength++;
            }
            else
            {
                break;
            }
        }

        // Build relative path: go up for remaining document segments, then down to target
        var upCount = documentSegments.Length - commonLength;
        var relativeParts = new List<string>();

        for (var i = 0; i < upCount; i++)
        {
            relativeParts.Add("..");
        }

        for (var i = commonLength; i < targetSegments.Length; i++)
        {
            relativeParts.Add(targetSegments[i]);
        }

        return string.Join("/", relativeParts);
    }

    /// <summary>
    /// Resolves a path to an absolute Resource Key.
    /// Paths starting with '/' are resolved from the project root.
    /// All other paths are resolved relative to the current document's folder.
    /// </summary>
    private Result<ResourceKey> ResolveResourcePath(string path)
    {
        string fullPath;

        if (path.StartsWith('/'))
        {
            // Project-root-relative path: strip the leading '/' and use as-is
            fullPath = path.Substring(1);
        }
        else
        {
            // Document-relative path: prepend document's folder
            var documentBasePath = GetDocumentBasePath();
            fullPath = string.IsNullOrEmpty(documentBasePath)
                ? path
                : $"{documentBasePath}/{path}";
        }

        var normalizedPath = NormalizeResourcePath(fullPath);
        var resourceKey = new ResourceKey(normalizedPath);
        var result = _resourceRegistry.NormalizeResourceKey(resourceKey);

        if (result.IsSuccess)
        {
            return Result<ResourceKey>.Ok(result.Value);
        }

        return Result<ResourceKey>.Fail($"Could not resolve resource path: {path}");
    }

    #region IHostDocument

    public void OnDocumentChanged()
    {
        ViewModel.OnDataChanged();
    }

    #endregion

    #region IHostInput

    public void OnLinkClicked(string href)
    {
        if (string.IsNullOrEmpty(href))
        {
            return;
        }

        // Remote URLs: open in system browser
        if (Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            OpenSystemBrowser(href);
            return;
        }

        // Resolve the path relative to the current document's folder
        try
        {
            var resolveResult = ResolveResourcePath(href);
            if (resolveResult.IsSuccess)
            {
                _commandService.Execute<IOpenDocumentCommand>(command =>
                {
                    command.FileResource = resolveResult.Value;
                });
                return;
            }

            // Could not resolve the link - show error asynchronously
            _ = ShowLinkErrorAsync(href);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to handle link click for '{href}': {ex.Message}");
            _ = ShowLinkErrorAsync(href);
        }
    }

    #endregion

    private async Task ShowLinkErrorAsync(string href)
    {
        var errorTitle = _stringLocalizer.GetString("Note_LinkError_Title");
        var errorMessage = _stringLocalizer.GetString("Note_LinkError_Message", href);
        await _dialogService.ShowAlertDialogAsync(errorTitle, errorMessage);
    }

    public override async Task<Result> SetFileResource(ResourceKey fileResource)
    {
        var filePath = _resourceRegistry.GetResourcePath(fileResource);

        if (_resourceRegistry.GetResource(fileResource).IsFailure)
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
        return await ViewModel.LoadContent();
    }

    public override async Task PrepareToClose()
    {
        _messengerService.UnregisterAll(this);

        Loaded -= NoteDocumentView_Loaded;

        ViewModel.ReloadRequested -= ViewModel_ReloadRequested;

        ViewModel.Cleanup();

        await base.PrepareToClose();
    }

    private void OnThemeChanged(object recipient, ThemeChangedMessage message)
    {
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

    private async void ViewModel_ReloadRequested(object? sender, EventArgs e)
    {
        // External file change detected - notify JS to reload
        // The dirty state conflict handling is done in the ViewModel before raising this event
        if (_noteHost is not null)
        {
            await _noteHost.NotifyExternalChangeAsync();
        }
    }
}
