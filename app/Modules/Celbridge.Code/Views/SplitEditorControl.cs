using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.WebView;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace Celbridge.Code.Views;

/// <summary>
/// A reusable control that combines a Monaco code editor with an optional preview panel.
/// The preview panel is rendered by an IPreviewRenderer implementation.
/// </summary>
public sealed partial class SplitEditorControl : UserControl
{
    private readonly ILogger<SplitEditorControl> _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IUserInterfaceService _userInterfaceService;

    private WebView2? _previewWebView;
    private bool _isPreviewInitialized;
    private bool _isPreviewUpdateInProgress;
    private string _lastPreviewContent = string.Empty;
    private IPreviewRenderer? _previewRenderer;
    private string _projectFolderPath = string.Empty;
    private string _documentPath = string.Empty;

    // Grid components
    private readonly Grid _rootGrid;
    private readonly ColumnDefinition _editorColumn;
    private readonly ColumnDefinition _splitterColumn;
    private readonly ColumnDefinition _previewColumn;
    private readonly Border _splitter;
    private readonly Grid _previewContainer;

    /// <summary>
    /// The Monaco editor control.
    /// </summary>
    public MonacoEditorControl MonacoEditor { get; }

    /// <summary>
    /// Whether the preview panel is currently visible.
    /// </summary>
    public bool IsPreviewVisible { get; private set; }

    /// <summary>
    /// Raised when the content changes in the Monaco editor.
    /// </summary>
    public event Action? ContentChanged;

    /// <summary>
    /// Raised when the editor receives focus.
    /// </summary>
    public event Action? EditorFocused;

    public SplitEditorControl()
    {
        _logger = ServiceLocator.AcquireService<ILogger<SplitEditorControl>>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _webViewFactory = ServiceLocator.AcquireService<IWebViewFactory>();
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();

        // Create the Monaco editor
        MonacoEditor = new MonacoEditorControl();
        MonacoEditor.ContentChanged += OnMonacoContentChanged;
        MonacoEditor.EditorFocused += OnMonacoEditorFocused;
        MonacoEditor.ScrollPositionChanged += OnMonacoScrollPositionChanged;

        // Build the UI programmatically
        _rootGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _editorColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        _splitterColumn = new ColumnDefinition { Width = new GridLength(0) };
        _previewColumn = new ColumnDefinition { Width = new GridLength(0) };

        _rootGrid.ColumnDefinitions.Add(_editorColumn);
        _rootGrid.ColumnDefinitions.Add(_splitterColumn);
        _rootGrid.ColumnDefinitions.Add(_previewColumn);

        // Add Monaco editor
        Grid.SetColumn(MonacoEditor, 0);
        _rootGrid.Children.Add(MonacoEditor);

        // Create splitter
        _splitter = new Border
        {
            Width = 1,
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(_splitter, 1);
        _rootGrid.Children.Add(_splitter);

        // Create preview container
        _previewContainer = new Grid
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(_previewContainer, 2);
        _rootGrid.Children.Add(_previewContainer);

        this.Content = _rootGrid;

        // Subscribe to theme changes
        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChanged);
    }

    /// <summary>
    /// Configures the split editor with a preview renderer.
    /// Call this before showing the preview.
    /// </summary>
    public void ConfigurePreview(IPreviewRenderer previewRenderer, string projectFolderPath, string documentPath)
    {
        _previewRenderer = previewRenderer;
        _projectFolderPath = projectFolderPath;
        _documentPath = documentPath;

        // Disable scroll-beyond-last-line for proper scroll sync with preview
        MonacoEditor.Options = new MonacoEditorOptions { ScrollBeyondLastLine = false };
    }

    /// <summary>
    /// Shows or hides the preview panel.
    /// </summary>
    public void SetPreviewVisible(bool visible)
    {
        if (visible == IsPreviewVisible)
        {
            return;
        }

        IsPreviewVisible = visible;
        UpdatePreviewLayout(visible);
    }

    /// <summary>
    /// Updates the document path for resolving relative resources.
    /// </summary>
    public void UpdateDocumentPath(string documentPath)
    {
        _documentPath = documentPath;

        if (_isPreviewInitialized && _previewRenderer is not null && _previewWebView?.CoreWebView2 is not null)
        {
            _ = _previewRenderer.SetDocumentContextAsync(_previewWebView.CoreWebView2, _documentPath, _projectFolderPath);
        }
    }

    /// <summary>
    /// Gets the current content from the Monaco editor.
    /// </summary>
    public Task<string> GetContentAsync() => MonacoEditor.GetContentAsync();

    /// <summary>
    /// Cleans up resources when the control is being disposed.
    /// </summary>
    public async Task CleanupAsync()
    {
        MonacoEditor.ContentChanged -= OnMonacoContentChanged;
        MonacoEditor.EditorFocused -= OnMonacoEditorFocused;
        MonacoEditor.ScrollPositionChanged -= OnMonacoScrollPositionChanged;

        _messengerService.UnregisterAll(this);

        await MonacoEditor.CleanupAsync();
        await CleanupPreviewAsync();
    }

    private void OnMonacoContentChanged()
    {
        ContentChanged?.Invoke();

        // Update preview if visible
        if (IsPreviewVisible && !_isPreviewUpdateInProgress)
        {
            _ = UpdatePreviewAsync();
        }
    }

    private void OnMonacoEditorFocused()
    {
        EditorFocused?.Invoke();
    }

    private void OnMonacoScrollPositionChanged(double scrollPercentage)
    {
        if (IsPreviewVisible && _isPreviewInitialized && _previewRenderer is not null && _previewWebView?.CoreWebView2 is not null)
        {
            _ = _previewRenderer.ScrollToPositionAsync(_previewWebView.CoreWebView2, scrollPercentage);
        }
    }

    private void UpdatePreviewLayout(bool showPreview)
    {
        if (showPreview)
        {
            _editorColumn.Width = new GridLength(1, GridUnitType.Star);
            _splitterColumn.Width = new GridLength(1, GridUnitType.Pixel);
            _previewColumn.Width = new GridLength(1, GridUnitType.Star);
            _splitter.Visibility = Visibility.Visible;
            _previewContainer.Visibility = Visibility.Visible;

            _ = InitializePreviewAsync();
        }
        else
        {
            _editorColumn.Width = new GridLength(1, GridUnitType.Star);
            _splitterColumn.Width = new GridLength(0);
            _previewColumn.Width = new GridLength(0);
            _splitter.Visibility = Visibility.Collapsed;
            _previewContainer.Visibility = Visibility.Collapsed;
        }
    }

    private async Task InitializePreviewAsync()
    {
        if (_previewRenderer is null)
        {
            _logger.LogWarning("Cannot initialize preview: no preview renderer configured");
            return;
        }

        if (_isPreviewInitialized)
        {
            await UpdatePreviewAsync();
            return;
        }

        try
        {
            _previewWebView = await _webViewFactory.AcquireAsync();
            _previewContainer.Children.Add(_previewWebView);

            // Set up virtual host mapping for preview assets
            _previewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                _previewRenderer.PreviewHostName,
                _previewRenderer.PreviewAssetFolder,
                CoreWebView2HostResourceAccessKind.Allow);

            // Set up shared celbridge-client mapping
            _previewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "shared.celbridge",
                "Celbridge.WebView/Web",
                CoreWebView2HostResourceAccessKind.Allow);

            // Allow the renderer to configure additional mappings
            await _previewRenderer.ConfigureWebViewAsync(_previewWebView.CoreWebView2, _projectFolderPath);

            // Handle messages from preview
            _previewWebView.CoreWebView2.WebMessageReceived += OnPreviewWebMessageReceived;

            // Apply theme
            ApplyThemeToPreview();

            // Navigate to preview page
            _previewWebView.CoreWebView2.Navigate(_previewRenderer.PreviewPageUrl);

            // Wait for navigation to complete
            var tcs = new TaskCompletionSource();
            void NavigationCompleted(object? s, CoreWebView2NavigationCompletedEventArgs args)
            {
                _previewWebView.CoreWebView2.NavigationCompleted -= NavigationCompleted;
                tcs.TrySetResult();
            }
            _previewWebView.CoreWebView2.NavigationCompleted += NavigationCompleted;

            var timeout = Task.Delay(TimeSpan.FromSeconds(5));
            var completed = await Task.WhenAny(tcs.Task, timeout);

            if (completed == timeout)
            {
                _logger.LogWarning("Preview navigation timed out");
            }

            _isPreviewInitialized = true;

            // Set document context
            await _previewRenderer.SetDocumentContextAsync(_previewWebView.CoreWebView2, _documentPath, _projectFolderPath);

            // Initial preview update
            await UpdatePreviewAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize preview");
        }
    }

    private async Task UpdatePreviewAsync()
    {
        if (_previewWebView?.CoreWebView2 is null || !_isPreviewInitialized || _previewRenderer is null)
        {
            return;
        }

        if (_isPreviewUpdateInProgress)
        {
            return;
        }

        _isPreviewUpdateInProgress = true;

        try
        {
            var content = await MonacoEditor.GetContentAsync();

            if (content == _lastPreviewContent || !IsPreviewVisible)
            {
                return;
            }

            _lastPreviewContent = content;

            await _previewRenderer.UpdatePreviewAsync(_previewWebView.CoreWebView2, content);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to update preview");
        }
        finally
        {
            _isPreviewUpdateInProgress = false;
        }
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
        if (_previewRenderer is null)
        {
            return;
        }

        try
        {
            var message = args.WebMessageAsJson;
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            var type = root.GetProperty("type").GetString() ?? string.Empty;

            // Let the renderer handle the message first
            var handled = _previewRenderer.HandlePreviewMessage(
                type,
                root,
                OpenLocalResource,
                OpenExternalUrl);

            if (!handled)
            {
                // Handle common message types
                switch (type)
                {
                    case "syncToEditor":
                        var percentage = root.GetProperty("percentage").GetDouble();
                        _ = MonacoEditor.ScrollToPercentageAsync(percentage);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to handle preview web message");
        }
    }

    private void OpenLocalResource(string relativePath)
    {
        if (string.IsNullOrEmpty(_documentPath))
        {
            return;
        }

        var documentDir = Path.GetDirectoryName(_documentPath);
        if (string.IsNullOrEmpty(documentDir))
        {
            return;
        }

        var fullPath = Path.GetFullPath(Path.Combine(documentDir, relativePath));

        if (!fullPath.StartsWith(_projectFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning($"Link path is outside project folder: {relativePath}");
            return;
        }

        var resourcePath = fullPath.Substring(_projectFolderPath.Length).TrimStart(Path.DirectorySeparatorChar);
        var resourceKey = new ResourceKey(resourcePath.Replace(Path.DirectorySeparatorChar, '/'));

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
