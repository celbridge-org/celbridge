# WebView Bridge: Developer Guide

This guide explains how to create new WebView2-based editors using the WebView Bridge JSON-RPC communication layer.

## Overview

The WebView Bridge provides a standardized JSON-RPC 2.0 communication layer between WebView2-hosted editors and the Celbridge host application. It replaces ad-hoc message passing with:

- Promise-based async/await API in JavaScript
- Automatic request/response correlation
- Typed handler registration in C#
- Standardized initialization handshake

## Quick Start

### 1. Create the HTML/JavaScript Editor

Create your editor's HTML file in your module's `Web/` directory:

```html
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>My Editor</title>
    <style>
        /* Your editor styles */
    </style>
</head>
<body>
    <div id="editor-container"></div>
    
    <script type="module">
        import { getBridge } from 'https://shared.celbridge/webview-bridge.js';
        
        const bridge = getBridge();
        
        // Initialize the bridge - this loads content and configuration
        async function init() {
            try {
                const { content, metadata, localization, theme } = await bridge.initialize();
                
                // Set up your editor with the content
                document.getElementById('editor-container').textContent = content;
                
                // Apply theme
                document.body.classList.add(theme.isDark ? 'dark' : 'light');
                
                // Register for theme changes
                bridge.theme.onChanged((newTheme) => {
                    document.body.classList.toggle('dark', newTheme.isDark);
                    document.body.classList.toggle('light', !newTheme.isDark);
                });
                
                // Register for save requests (auto-save from host)
                bridge.document.onRequestSave(async () => {
                    const currentContent = getEditorContent();
                    await bridge.document.save(currentContent);
                });
                
                // Notify host when content changes
                editor.on('change', () => {
                    bridge.document.notifyChanged();
                });
                
            } catch (error) {
                console.error('Initialization failed:', error);
            }
        }
        
        init();
    </script>
</body>
</html>
```

### 2. Create the C# View

Create your document view class:

```csharp
public partial class MyEditorDocumentView : UserControl, IDocumentView
{
    private WebViewBridge? _bridge;
    private bool _isDirty;
    
    // ... standard properties ...
    
    private async Task InitializeWebViewAsync()
    {
        await WebView.EnsureCoreWebView2Async();
        
        // Map shared assets for the bridge JS module
        WebView2Helper.MapSharedAssets(WebView.CoreWebView2);
        
        // Map your editor's web content
        WebView2Helper.MapLocalFolder(
            WebView.CoreWebView2,
            "myeditor.celbridge",
            Path.Combine(AppContext.BaseDirectory, "Web", "MyEditor"));
        
        // Create the bridge
        var channel = new WebView2MessageChannel(WebView.CoreWebView2);
        _bridge = new WebViewBridge(channel);
        
        // Register handlers
        _bridge.OnInitialize(HandleInitializeAsync);
        _bridge.Document.OnSave(HandleDocumentSaveAsync);
        _bridge.Document.OnLoad(HandleDocumentLoadAsync);
        _bridge.Document.OnChanged(() => _isDirty = true);
        
        // Navigate to your editor
        WebView.CoreWebView2.Navigate("https://myeditor.celbridge/index.html");
    }
    
    private async Task<InitializeResult> HandleInitializeAsync(InitializeParams request)
    {
        // Validate protocol version
        if (request.ProtocolVersion != "1.0")
        {
            throw new BridgeException(
                JsonRpcErrorCodes.InvalidVersion,
                "Unsupported protocol version");
        }
        
        // Load content
        var content = await File.ReadAllTextAsync(_filePath);
        
        // Get metadata
        var metadata = new DocumentMetadata(_filePath, _resourceKey, Path.GetFileName(_filePath));
        
        // Get localization strings
        var localization = WebViewLocalizationHelper.GetStrings(_stringLocalizer, "MyEditor_");
        
        // Get theme info
        var theme = new ThemeInfo(_themeService.CurrentTheme, _themeService.IsDarkTheme);
        
        return new InitializeResult(content, metadata, localization, theme);
    }
    
    private async Task<SaveResult> HandleDocumentSaveAsync(SaveParams request)
    {
        try
        {
            await File.WriteAllTextAsync(_filePath, request.Content);
            _isDirty = false;
            return new SaveResult(true);
        }
        catch (Exception ex)
        {
            return new SaveResult(false, ex.Message);
        }
    }
    
    private async Task<LoadResult> HandleDocumentLoadAsync(LoadParams request)
    {
        var content = await File.ReadAllTextAsync(_filePath);
        var metadata = request.IncludeMetadata 
            ? new DocumentMetadata(_filePath, _resourceKey, Path.GetFileName(_filePath))
            : null;
        return new LoadResult(content, metadata);
    }
}
```

## JavaScript API Reference

### Initialization

```javascript
import { getBridge } from 'https://shared.celbridge/webview-bridge.js';

const bridge = getBridge();
const { content, metadata, localization, theme } = await bridge.initialize();
```

The `initialize()` method must be called before any other operations. It returns:

| Field | Type | Description |
|-------|------|-------------|
| `content` | `string` | The document content |
| `metadata` | `DocumentMetadata` | File path, resource key, file name |
| `localization` | `Object<string, string>` | Localized strings dictionary |
| `theme` | `ThemeInfo` | Current theme info (`name`, `isDark`) |

### Document Operations

| Method | Description |
|--------|-------------|
| `bridge.document.load()` | Reloads content from disk (for external changes) |
| `bridge.document.save(content)` | Saves content to disk |
| `bridge.document.getMetadata()` | Gets document metadata |
| `bridge.document.notifyChanged()` | Signals content has been modified (notification) |

#### Binary Content (for spreadsheets, etc.)

| Method | Description |
|--------|-------------|
| `bridge.document.saveBinary(base64)` | Saves base64-encoded binary content |
| `bridge.document.loadBinary()` | Loads binary content as base64 |

### Dialog Operations

| Method | Description |
|--------|-------------|
| `bridge.dialog.pickImage(['.png', '.jpg'])` | Opens image picker |
| `bridge.dialog.pickFile(['.md', '.txt'])` | Opens file picker |
| `bridge.dialog.alert(title, message)` | Shows alert dialog |

### Event Handlers

| Event | Description |
|-------|-------------|
| `bridge.document.onExternalChange(handler)` | File changed externally |
| `bridge.document.onRequestSave(handler)` | Host requests a save (auto-save) |
| `bridge.theme.onChanged(handler)` | Theme changed |
| `bridge.localization.onUpdated(handler)` | Localization strings updated |

### Debug Logging

```javascript
bridge.setLogLevel('debug'); // 'none' | 'error' | 'warn' | 'debug'
```

## C# API Reference

### Creating a Bridge

```csharp
var channel = new WebView2MessageChannel(WebView.CoreWebView2);
var bridge = new WebViewBridge(channel, logger);
bridge.EnableDetailedLogging = true; // Optional
```

### Handler Registration

| Method | Description |
|--------|-------------|
| `bridge.OnInitialize(handler)` | Handle `bridge/initialize` |
| `bridge.Document.OnSave(handler)` | Handle `document/save` |
| `bridge.Document.OnLoad(handler)` | Handle `document/load` |
| `bridge.Document.OnGetMetadata(handler)` | Handle `document/getMetadata` |
| `bridge.Document.OnChanged(handler)` | Handle `document/changed` notification |
| `bridge.Document.OnSaveBinary(handler)` | Handle `document/saveBinary` |
| `bridge.Document.OnLoadBinary(handler)` | Handle `document/loadBinary` |
| `bridge.Document.OnLinkClicked(handler)` | Handle `link/clicked` notification |
| `bridge.Dialog.OnPickImage(handler)` | Handle `dialog/pickImage` |
| `bridge.Dialog.OnPickFile(handler)` | Handle `dialog/pickFile` |
| `bridge.Dialog.OnAlert(handler)` | Handle `dialog/alert` |

### Sending Notifications

```csharp
bridge.Document.NotifyExternalChange();  // File changed externally
bridge.Document.RequestSave();           // Request JS to save content
bridge.Theme.NotifyChanged(themeInfo);   // Theme changed
```

### Contract Types

All request/response types are defined in `WebViewBridgeTypes.cs`:

- `InitializeParams`, `InitializeResult`
- `LoadParams`, `LoadResult`
- `SaveParams`, `SaveResult`
- `PickImageParams`, `PickImageResult`
- `PickFileParams`, `PickFileResult`
- `AlertParams`, `AlertResult`
- `DocumentMetadata`, `ThemeInfo`
- Binary variants: `SaveBinaryParams`, `SaveBinaryResult`, `LoadBinaryResult`

### Error Handling

Throw `BridgeException` for structured errors:

```csharp
throw new BridgeException(
    JsonRpcErrorCodes.InvalidParams,
    "File not found",
    new { path = filePath });
```

Unhandled exceptions are automatically wrapped as JSON-RPC Internal Errors.

## Architecture Patterns

### Auto-Save Flow

1. Host timer fires, calls `bridge.Document.RequestSave()`
2. JS receives `document/requestSave` notification
3. JS handler gets current content and calls `bridge.document.save(content)`
4. C# `OnSave` handler saves to disk, returns `SaveResult`

### External Change Flow

1. Host file watcher detects change
2. Host checks `_isDirty` flag (set by `document/changed` notifications)
3. If clean: Host calls `bridge.Document.NotifyExternalChange()`
4. JS receives notification, calls `bridge.document.load()` to refresh
5. If dirty: Host shows conflict dialog first

### Theme Change Flow

1. Host detects theme change
2. Host calls `bridge.Theme.NotifyChanged(themeInfo)`
3. JS receives `theme/changed` notification
4. JS handler updates UI (e.g., body class)

## Virtual Host Mapping

Use `WebView2Helper` to map local folders to virtual hosts:

```csharp
// Map shared assets (required for bridge JS module)
WebView2Helper.MapSharedAssets(WebView.CoreWebView2);

// Map your editor's content
WebView2Helper.MapLocalFolder(
    WebView.CoreWebView2,
    "myeditor.celbridge",       // Virtual host name
    physicalPath);               // Physical folder path
```

## Existing Editor Examples

- **Markdown**: `Modules/Celbridge.Markdown/Views/MarkdownDocumentView.xaml.cs`
- **Spreadsheet**: `Modules/Celbridge.Spreadsheet/Views/SpreadsheetDocumentView.xaml.cs`
- **Screenplay**: `Modules/Celbridge.Screenplay/Views/SceneDocumentView.xaml.cs`

## Testing

### JavaScript Tests

The shared bridge has Vitest tests in `Core/Celbridge.UserInterface/Web/`:

```bash
cd Core/Celbridge.UserInterface/Web
npm ci
npm test
```

### C# Tests

Bridge tests are in `Celbridge.Tests/Helpers/WebViewBridgeTests.cs` using a `MockWebViewMessageChannel`.

### Editor-Specific Testing

Editor-specific JavaScript is tested manually through the editor. The bridge unit tests verify the communication layer; editor behavior is validated through manual testing.
