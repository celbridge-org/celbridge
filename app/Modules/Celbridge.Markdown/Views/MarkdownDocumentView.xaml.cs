using Celbridge.Code.Views;
using Celbridge.Commands;
using Celbridge.Documents.Views;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Markdown.ViewModels;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.WebView;
using Celbridge.Workspace;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace Celbridge.Markdown.Views;

/// <summary>
/// Document view for editing markdown files using the Monaco editor.
/// Provides source-first editing of .md files with syntax highlighting and optional preview.
/// </summary>
public sealed partial class MarkdownDocumentView : DocumentView
{
    private readonly ILogger<MarkdownDocumentView> _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IUserInterfaceService _userInterfaceService;

    private WebView2? _previewWebView;
    private bool _isPreviewInitialized;
    private bool _isPreviewUpdateInProgress;
    private string _lastPreviewContent = string.Empty;

    public MarkdownDocumentViewModel ViewModel { get; }

    public override ResourceKey FileResource => ViewModel.FileResource;

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public MarkdownDocumentView()
    {
        this.InitializeComponent();

        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();

        _logger = ServiceLocator.AcquireService<ILogger<MarkdownDocumentView>>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _webViewFactory = ServiceLocator.AcquireService<IWebViewFactory>();
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();

        ViewModel = ServiceLocator.AcquireService<MarkdownDocumentViewModel>();

        // Set up content loader callback for Monaco to pull content when needed
        MonacoEditor.ContentLoader = LoadContentFromDiskAsync;

        // Subscribe to MonacoEditorControl events
        MonacoEditor.ContentChanged += OnMonacoContentChanged;
        MonacoEditor.EditorFocused += OnMonacoEditorFocused;

        // Subscribe to ViewModel events
        ViewModel.ReloadRequested += OnViewModelReloadRequested;
        ViewModel.PreviewVisibilityChanged += OnPreviewVisibilityChanged;

        // Subscribe to theme changes for preview
        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChanged);
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

        return await Task.FromResult(Result.Ok());
    }

    public override async Task<Result> LoadContent()
    {
        // Load file content via ViewModel
        var loadResult = await ViewModel.LoadDocument();
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load content for resource: {ViewModel.FileResource}")
                .WithErrors(loadResult);
        }

        var content = loadResult.Value;

        // Initialize Monaco with the content, using markdown language
        var initResult = await MonacoEditor.InitializeAsync(
            content,
            "markdown",
            ViewModel.FilePath,
            ViewModel.FileResource.ToString());

        return initResult;
    }

    /// <summary>
    /// Loads content from disk. Used as the ContentLoader callback for Monaco.
    /// </summary>
    private async Task<string> LoadContentFromDiskAsync()
    {
        var loadResult = await ViewModel.LoadDocument();
        if (loadResult.IsFailure)
        {
            _logger.LogError(loadResult, $"Failed to load content for resource: {ViewModel.FileResource}");
            return string.Empty;
        }

        return loadResult.Value;
    }

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
    }

    public override async Task<Result> SaveDocument()
    {
        try
        {
            // Get current content from Monaco
            var content = await MonacoEditor.GetContentAsync();

            // Save via ViewModel
            return await ViewModel.SaveDocument(content);
        }
        catch (Exception ex)
        {
            return Result.Fail("Failed to save document")
                .WithException(ex);
        }
    }

    public override async Task PrepareToClose()
    {
        try
        {
            // Unsubscribe from events and clear callback
            MonacoEditor.ContentChanged -= OnMonacoContentChanged;
            MonacoEditor.EditorFocused -= OnMonacoEditorFocused;
            MonacoEditor.ContentLoader = null;
            ViewModel.ReloadRequested -= OnViewModelReloadRequested;
            ViewModel.PreviewVisibilityChanged -= OnPreviewVisibilityChanged;
            _messengerService.UnregisterAll(this);

            // Cleanup ViewModel
            ViewModel.Cleanup();

            // Cleanup Monaco control
            await MonacoEditor.CleanupAsync();

            // Cleanup preview WebView
            await CleanupPreviewAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while preparing MarkdownDocumentView to close");
        }
    }

    private void OnMonacoContentChanged()
    {
        // Mark document as having unsaved changes
        ViewModel.OnTextChanged();

        // Update preview if visible and not already updating
        if (ViewModel.IsPreviewVisible && !_isPreviewUpdateInProgress)
        {
            _ = UpdatePreviewAsync();
        }
    }

    private void OnMonacoEditorFocused()
    {
        // Notify the system that this document view has focus
        var message = new DocumentViewFocusedMessage(ViewModel.FileResource);
        _messengerService.Send(message);
    }

    private void OnViewModelReloadRequested(object? sender, EventArgs e)
    {
        // External file change detected - notify Monaco to reload
        MonacoEditor.NotifyExternalChange();
    }

    private void PreviewToggleButton_Click(object sender, RoutedEventArgs e)
    {
        // Toggle is handled by binding, but we need to trigger the layout update
        UpdatePreviewLayout(ViewModel.IsPreviewVisible);
    }

    private void OnPreviewVisibilityChanged(object? sender, bool isVisible)
    {
        UpdatePreviewLayout(isVisible);
    }

    private void UpdatePreviewLayout(bool showPreview)
    {
        if (showPreview)
        {
            // Show preview in split mode
            EditorColumn.Width = new GridLength(1, GridUnitType.Star);
            SplitterColumn.Width = new GridLength(1, GridUnitType.Pixel);
            PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
            Splitter.Visibility = Visibility.Visible;
            PreviewContainer.Visibility = Visibility.Visible;

            // Initialize and update preview
            _ = InitializePreviewAsync();
        }
        else
        {
            // Hide preview, show editor full width
            EditorColumn.Width = new GridLength(1, GridUnitType.Star);
            SplitterColumn.Width = new GridLength(0);
            PreviewColumn.Width = new GridLength(0);
            Splitter.Visibility = Visibility.Collapsed;
            PreviewContainer.Visibility = Visibility.Collapsed;
        }
    }

    private async Task InitializePreviewAsync()
    {
        if (_isPreviewInitialized)
        {
            await UpdatePreviewAsync();
            return;
        }

        try
        {
            // Acquire WebView from factory
            _previewWebView = await _webViewFactory.AcquireAsync();
            PreviewContainer.Children.Add(_previewWebView);

            // Set up virtual host mapping for preview assets
            _previewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "markdown-preview.celbridge",
                "Celbridge.Markdown/Web/markdown-preview",
                CoreWebView2HostResourceAccessKind.Allow);

            // Set up shared celbridge-client mapping
            _previewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "shared.celbridge",
                "Celbridge.WebView/Web",
                CoreWebView2HostResourceAccessKind.Allow);

            // Map the project folder so local image paths resolve correctly
            var projectFolder = _resourceRegistry.ProjectFolderPath;
            if (!string.IsNullOrEmpty(projectFolder))
            {
                _previewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "project.celbridge",
                    projectFolder,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            // Handle messages from preview (link clicks)
            _previewWebView.CoreWebView2.WebMessageReceived += OnPreviewWebMessageReceived;

            // Apply theme
            ApplyThemeToPreview();

            // Navigate to preview page
            _previewWebView.CoreWebView2.Navigate("https://markdown-preview.celbridge/index.html");

            // Wait for navigation to complete
            var tcs = new TaskCompletionSource();
            void NavigationCompleted(object? s, CoreWebView2NavigationCompletedEventArgs args)
            {
                _previewWebView.CoreWebView2.NavigationCompleted -= NavigationCompleted;
                tcs.TrySetResult();
            }
            _previewWebView.CoreWebView2.NavigationCompleted += NavigationCompleted;

            // Wait with timeout
            var timeout = Task.Delay(TimeSpan.FromSeconds(5));
            var completed = await Task.WhenAny(tcs.Task, timeout);

            if (completed == timeout)
            {
                _logger.LogWarning("Preview navigation timed out");
            }

            _isPreviewInitialized = true;

            // Set the document base path for resolving relative resources
            await SetDocumentBasePathAsync();

            // Initial preview update
            await UpdatePreviewAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize markdown preview");
        }
    }

    /// <summary>
    /// Sets the document base path in the preview for resolving relative resource paths.
    /// The base path is the directory containing the markdown file, relative to the project root.
    /// </summary>
    private async Task SetDocumentBasePathAsync()
    {
        if (_previewWebView?.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            // Get the document's directory path relative to the project root
            var documentDir = Path.GetDirectoryName(ViewModel.FilePath);
            var projectFolder = _resourceRegistry.ProjectFolderPath;

            var basePath = string.Empty;
            if (!string.IsNullOrEmpty(documentDir) && !string.IsNullOrEmpty(projectFolder))
            {
                if (documentDir.StartsWith(projectFolder, StringComparison.OrdinalIgnoreCase))
                {
                    basePath = documentDir.Substring(projectFolder.Length)
                        .TrimStart(Path.DirectorySeparatorChar)
                        .Replace(Path.DirectorySeparatorChar, '/');
                }
            }

            // Escape the path for JavaScript
            var escapedPath = basePath.Replace("\\", "\\\\").Replace("`", "\\`");
            var script = $"window.celbridge.setBasePath(`{escapedPath}`);";
            await _previewWebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to set document base path");
        }
    }

    private async Task UpdatePreviewAsync()
    {
        if (_previewWebView?.CoreWebView2 is null || !_isPreviewInitialized)
        {
            return;
        }

        // Prevent concurrent updates
        if (_isPreviewUpdateInProgress)
        {
            return;
        }

        _isPreviewUpdateInProgress = true;

        try
        {
            // Get current content from Monaco
            var content = await MonacoEditor.GetContentAsync();

            // Only update if content changed and preview is still visible
            if (content == _lastPreviewContent || !ViewModel.IsPreviewVisible)
            {
                return;
            }

            _lastPreviewContent = content;

            // Escape the content for JavaScript
            var escapedContent = EscapeForJavaScript(content);

            // Call the preview's updatePreview function
            var script = $"window.celbridge.updatePreview(`{escapedContent}`);";
            await _previewWebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to update markdown preview");
        }
        finally
        {
            _isPreviewUpdateInProgress = false;
        }
    }

    private static string EscapeForJavaScript(string content)
    {
        // Escape backticks and backslashes for template literal
        return content
            .Replace("\\", "\\\\")
            .Replace("`", "\\`")
            .Replace("$", "\\$");
    }

    private async Task CleanupPreviewAsync()
    {
        if (_previewWebView is not null)
        {
            _previewWebView.CoreWebView2.WebMessageReceived -= OnPreviewWebMessageReceived;
            _previewWebView.Close();
            _previewWebView = null;
        }

        _isPreviewInitialized = false;

        await Task.CompletedTask;
    }

    private void OnPreviewWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var message = args.WebMessageAsJson;
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            var type = root.GetProperty("type").GetString();
            var href = root.GetProperty("href").GetString();

            if (string.IsNullOrEmpty(href))
            {
                return;
            }

            switch (type)
            {
                case "openResource":
                    OpenLocalResource(href);
                    break;

                case "openExternal":
                    OpenExternalUrl(href);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to handle preview web message");
        }
    }

    private void OpenLocalResource(string relativePath)
    {
        // Resolve the path relative to the current markdown file's directory
        var documentDir = Path.GetDirectoryName(ViewModel.FilePath);
        if (string.IsNullOrEmpty(documentDir))
        {
            return;
        }

        var fullPath = Path.GetFullPath(Path.Combine(documentDir, relativePath));

        // Convert to resource key
        var projectFolder = _resourceRegistry.ProjectFolderPath;
        if (!fullPath.StartsWith(projectFolder, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning($"Link path is outside project folder: {relativePath}");
            return;
        }

        var resourcePath = fullPath.Substring(projectFolder.Length).TrimStart(Path.DirectorySeparatorChar);
        var resourceKey = new ResourceKey(resourcePath.Replace(Path.DirectorySeparatorChar, '/'));

        // Open the document
        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resourceKey;
        });
    }

    private void OpenExternalUrl(string url)
    {
        _commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = url;
        });
    }

    private void OnThemeChanged(object recipient, ThemeChangedMessage message)
    {
        ApplyThemeToPreview();
    }

    private void ApplyThemeToPreview()
    {
        if (_previewWebView?.CoreWebView2 is null)
        {
            return;
        }

        var theme = _userInterfaceService.UserInterfaceTheme;
        _previewWebView.CoreWebView2.Profile.PreferredColorScheme = theme == UserInterfaceTheme.Dark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;
    }
}
