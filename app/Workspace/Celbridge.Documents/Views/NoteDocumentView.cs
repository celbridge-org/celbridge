using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Celbridge.Documents.Views;

public sealed partial class NoteDocumentView : DocumentView
{
    private ILogger _logger;
    private ICommandService _commandService;
    private IMessengerService _messengerService;
    private IResourceRegistry _resourceRegistry;

    public NoteDocumentViewModel ViewModel { get; }

    private WebView2? _webView;

    // Track save state to prevent race conditions
    private bool _isSaveInProgress = false;
    private bool _hasPendingSave = false;

    public NoteDocumentView(
        IServiceProvider serviceProvider,
        ILogger<NoteDocumentView> logger,
        ICommandService commandService,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        ViewModel = serviceProvider.GetRequiredService<NoteDocumentViewModel>();

        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        Loaded += NoteDocumentView_Loaded;

        ViewModel.ReloadRequested += ViewModel_ReloadRequested;

        this.DataContext(ViewModel);
    }

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
    }

    public override async Task<Result> SaveDocument()
    {
        Guard.IsNotNull(_webView);

        if (_isSaveInProgress)
        {
            _hasPendingSave = true;
            _logger.LogDebug("Save already in progress, queuing pending save");
            return Result.Ok();
        }

        _isSaveInProgress = true;
        _hasPendingSave = false;

        SendMessageToJS(new { type = "request-save" });

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
            var webView = new WebView2();
            await webView.EnsureCoreWebView2Async();

            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("note.celbridge",
                "Celbridge.Documents/Web/Note",
                CoreWebView2HostResourceAccessKind.Allow);

            // Map the project folder so resource key image paths resolve correctly
            var projectFolder = _resourceRegistry.ProjectFolderPath;
            if (!string.IsNullOrEmpty(projectFolder))
            {
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "project.celbridge",
                    projectFolder,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            webView.DefaultBackgroundColor = Colors.Transparent;

            webView.CoreWebView2.Settings.IsWebMessageEnabled = true;

            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

            await WebView2Helper.InjectKeyboardShortcutHandlerAsync(webView.CoreWebView2);

            // Open external links in system browser
            webView.NavigationStarting += (s, args) =>
            {
                var uri = args.Uri;
                if (uri != null && !uri.StartsWith("https://note.celbridge"))
                {
                    args.Cancel = true;
                    OpenSystemBrowser(uri);
                }
            };

            webView.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                args.Handled = true;
                var uri = args.Uri;
                OpenSystemBrowser(uri);
            };

            webView.GotFocus += WebView_GotFocus;

            _webView = webView;

            // Show the WebView immediately so the status is visible
            this.Content = _webView;

            webView.CoreWebView2.Navigate("https://note.celbridge/index.html");

            bool isEditorReady = false;
            TypedEventHandler<WebView2, CoreWebView2WebMessageReceivedEventArgs> onWebMessageReceived = (sender, e) =>
            {
                var message = e.TryGetWebMessageAsString();

                if (WebView2Helper.HandleKeyboardShortcut(message))
                {
                    return;
                }

                try
                {
                    using var doc = JsonDocument.Parse(message);
                    var type = doc.RootElement.GetProperty("type").GetString();
                    if (type == "editor-ready")
                    {
                        isEditorReady = true;
                        return;
                    }
                }
                catch
                {
                    // Not JSON or doesn't have type field
                }

                _logger.LogError($"Note: Expected 'editor-ready' message, but received: '{message}'");
            };

            webView.WebMessageReceived += onWebMessageReceived;

            // Wait for editor_ready with a timeout to avoid hanging indefinitely
            var timeout = TimeSpan.FromSeconds(30);
            var elapsed = TimeSpan.Zero;
            var interval = TimeSpan.FromMilliseconds(50);

            while (!isEditorReady)
            {
                await Task.Delay(interval);
                elapsed += interval;
                if (elapsed >= timeout)
                {
                    _logger.LogError("Note: Timed out waiting for 'editor-ready' message from WebView");
                    return;
                }
            }

            webView.WebMessageReceived -= onWebMessageReceived;

            await LoadNoteContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Note Web View.");
        }
    }

    private async Task LoadNoteContent()
    {
        Guard.IsNotNull(_webView);

        var filePath = ViewModel.FilePath;

        if (!File.Exists(filePath))
        {
            _logger.LogWarning($"Note: Cannot load - file does not exist: {filePath}");
            return;
        }

        try
        {
            var docJson = await ViewModel.LoadNoteDocJson();
            var projectBaseUrl = "https://project.celbridge/";

            SendMessageToJS(new { type = "load-doc", payload = new { content = docJson, projectBaseUrl } });

            _webView.WebMessageReceived -= WebView_WebMessageReceived;
            _webView.WebMessageReceived += WebView_WebMessageReceived;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to load note: {filePath}");
        }
    }

    private void SendMessageToJS(object message)
    {
        Guard.IsNotNull(_webView);
        var json = JsonSerializer.Serialize(message);
        _webView.CoreWebView2.PostWebMessageAsString(json);
    }

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

    private async void WebView_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var webMessage = args.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(webMessage))
        {
            _logger.LogError("Invalid web message received");
            return;
        }

        if (WebView2Helper.HandleKeyboardShortcut(webMessage))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(webMessage);
            var type = doc.RootElement.GetProperty("type").GetString();

            switch (type)
            {
                case "doc-changed":
                    ViewModel.OnDataChanged();
                    break;

                case "save-response":
                    if (_isSaveInProgress)
                    {
                        var content = doc.RootElement
                            .GetProperty("payload")
                            .GetProperty("content")
                            .GetString();

                        if (!string.IsNullOrEmpty(content))
                        {
                            await SaveNoteContent(content);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Received save-response while no save was in progress");
                    }
                    break;

                default:
                    _logger.LogWarning($"Note: Received unknown message type: '{type}'");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Note: Failed to parse web message: '{webMessage[..Math.Min(50, webMessage.Length)]}'. Exception: {ex.Message}");
        }
    }

    private async Task SaveNoteContent(string docJson)
    {
        var saveResult = await ViewModel.SaveNoteToFile(docJson);

        if (saveResult.IsFailure)
        {
            _logger.LogError(saveResult, "Failed to save note data");
        }

        _isSaveInProgress = false;

        if (_hasPendingSave)
        {
            _logger.LogDebug("Processing pending save request");
            _hasPendingSave = false;
            ViewModel.OnDataChanged();
        }
    }

    public override async Task PrepareToClose()
    {
        Loaded -= NoteDocumentView_Loaded;

        ViewModel.ReloadRequested -= ViewModel_ReloadRequested;

        ViewModel.Cleanup();

        if (_webView != null)
        {
            _webView.WebMessageReceived -= WebView_WebMessageReceived;
            _webView.GotFocus -= WebView_GotFocus;

            _webView.Close();
            _webView = null;
        }

        await base.PrepareToClose();
    }

    private void WebView_GotFocus(object sender, RoutedEventArgs e)
    {
        var message = new DocumentViewFocusedMessage(ViewModel.FileResource);
        _messengerService.Send(message);
    }

    private async void ViewModel_ReloadRequested(object? sender, EventArgs e)
    {
        if (_webView != null)
        {
            await LoadNoteContent();
        }
    }
}
