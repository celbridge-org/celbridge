# Celbridge API: Developer Guide

Guide for creating WebView2-based editors using the Celbridge JSON-RPC communication layer.

## Overview

The Celbridge API provides JSON-RPC 2.0 communication between WebView2-hosted editors and the .NET host:

- Promise-based async/await API in JavaScript (`celbridge-api.js`)
- Typed handler registration in C# (`CelbridgeHost`)
- Automatic request/response correlation
- Browser-native theme detection via `prefers-color-scheme`

## Quick Start

### JavaScript Editor

```html
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>My Editor</title>
</head>
<body>
    <div id="editor-container"></div>

    <script type="module">
        import { getClient } from 'https://shared.celbridge/celbridge-api.js';

        const client = getClient();

        async function init() {
            const { content, metadata, localization } = await client.initialize();

            // Set up editor with content
            document.getElementById('editor-container').textContent = content;

            // Apply theme (uses browser's prefers-color-scheme)
            document.body.classList.add(client.theme.isDark ? 'dark' : 'light');

            // Theme changes
            client.theme.onChanged((theme) => {
                document.body.classList.toggle('dark', theme === 'Dark');
            });

            // Auto-save requests from host
            client.document.onRequestSave(async () => {
                await client.document.save(getEditorContent());
            });

            // Notify host of changes
            editor.on('change', () => client.document.notifyChanged());
        }

        init();
    </script>
</body>
</html>
```

### C# View

```csharp
public partial class MyEditorDocumentView : WebView2DocumentView
{
    private CelbridgeHost? _host;

    private async Task InitializeWebViewAsync()
    {
        await WebView.EnsureCoreWebView2Async();

        // Map shared assets (required for celbridge-api.js)
        WebView2Helper.MapSharedAssets(WebView.CoreWebView2);

        // Map editor content
        WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "myeditor.celbridge",
            "MyModule/Web/MyEditor",
            CoreWebView2HostResourceAccessKind.Allow);

        // Create host
        var channel = new WebView2MessageChannel(WebView.CoreWebView2);
        _host = new CelbridgeHost(channel);

        // Register handlers
        _host.OnInitialize(HandleInitializeAsync);
        _host.Document.OnSave(HandleSaveAsync);
        _host.Document.OnLoad(HandleLoadAsync);
        _host.Document.OnChanged(() => _isDirty = true);

        WebView.CoreWebView2.Navigate("https://myeditor.celbridge/index.html");
    }

    private async Task<InitializeResult> HandleInitializeAsync(InitializeParams request)
    {
        if (request.ProtocolVersion != "1.0")
            throw new BridgeException(JsonRpcErrorCodes.InvalidVersion, "Unsupported version");

        var content = await File.ReadAllTextAsync(_filePath);
        var metadata = new DocumentMetadata(_filePath, _resourceKey, Path.GetFileName(_filePath));
        var localization = WebViewLocalizationHelper.GetStrings(_stringLocalizer, "MyEditor_");

        return new InitializeResult(content, metadata, localization);
    }

    private async Task<SaveResult> HandleSaveAsync(SaveParams request)
    {
        await File.WriteAllTextAsync(_filePath, request.Content);
        return new SaveResult(true);
    }

    private async Task<LoadResult> HandleLoadAsync(LoadParams request)
    {
        var content = await File.ReadAllTextAsync(_filePath);
        var metadata = request.IncludeMetadata ? new DocumentMetadata(...) : null;
        return new LoadResult(content, metadata);
    }
}
```

## JavaScript API

### Initialization

```javascript
import { getClient } from 'https://shared.celbridge/celbridge-api.js';

const client = getClient();
const { content, metadata, localization } = await client.initialize();
```

Returns:
- `content` - Document content string
- `metadata` - `{ filePath, resourceKey, fileName }`
- `localization` - Dictionary of localized strings

### Document Operations

| Method | Description |
|--------|-------------|
| `client.document.load(options?)` | Reload content from disk |
| `client.document.save(content)` | Save content to disk |
| `client.document.getMetadata()` | Get document metadata |
| `client.document.notifyChanged()` | Signal content modified (notification) |
| `client.document.saveBinary(base64)` | Save base64-encoded binary |
| `client.document.loadBinary(options?)` | Load binary as base64 |
| `client.document.notifyImportComplete(success, error?)` | Signal import finished |

### Dialog Operations

| Method | Description |
|--------|-------------|
| `client.dialog.pickImage(extensions)` | Open image picker |
| `client.dialog.pickFile(extensions)` | Open file picker |
| `client.dialog.alert(title, message)` | Show alert dialog |

### Theme API

Theme is detected via the browser's `prefers-color-scheme` media query:

| Property/Method | Description |
|-----------------|-------------|
| `client.theme.current` | `'Light'` or `'Dark'` |
| `client.theme.isDark` | Boolean |
| `client.theme.onChanged(handler)` | Theme change callback |

### Event Handlers

| Event | Description |
|-------|-------------|
| `client.document.onExternalChange(handler)` | File changed externally |
| `client.document.onRequestSave(handler)` | Host requests save |
| `client.localization.onUpdated(handler)` | Localization updated |

### Debug Logging

```javascript
client.setLogLevel('debug'); // 'none' | 'error' | 'warn' | 'debug'
```

## C# API

### Creating a Host

```csharp
var channel = new WebView2MessageChannel(WebView.CoreWebView2);
var host = new CelbridgeHost(channel, logger);
host.EnableDetailedLogging = true;
```

### Handler Registration

| Method | Description |
|--------|-------------|
| `host.OnInitialize(handler)` | Handle `bridge/initialize` |
| `host.Document.OnSave(handler)` | Handle `document/save` |
| `host.Document.OnLoad(handler)` | Handle `document/load` |
| `host.Document.OnGetMetadata(handler)` | Handle `document/getMetadata` |
| `host.Document.OnChanged(handler)` | Handle `document/changed` notification |
| `host.Document.OnSaveBinary(handler)` | Handle `document/saveBinary` |
| `host.Document.OnLoadBinary(handler)` | Handle `document/loadBinary` |
| `host.Document.OnImportComplete(handler)` | Handle `import/complete` notification |
| `host.Document.OnLinkClicked(handler)` | Handle `link/clicked` notification |
| `host.Dialog.OnPickImage(handler)` | Handle `dialog/pickImage` |
| `host.Dialog.OnPickFile(handler)` | Handle `dialog/pickFile` |
| `host.Dialog.OnAlert(handler)` | Handle `dialog/alert` |

### Sending Notifications

```csharp
host.Document.NotifyExternalChange();    // File changed externally
host.Document.RequestSave();             // Request JS to save
host.Localization.NotifyUpdated(dict);   // Localization updated
```

### Contract Types

Defined in `CelbridgeHostTypes.cs`:

- **Initialize**: `InitializeParams`, `InitializeResult`
- **Document**: `LoadParams`, `LoadResult`, `SaveParams`, `SaveResult`, `GetMetadataParams`, `DocumentMetadata`
- **Binary**: `SaveBinaryParams`, `SaveBinaryResult`, `LoadBinaryResult`
- **Dialog**: `PickImageParams`, `PickImageResult`, `PickFileParams`, `PickFileResult`, `AlertParams`, `AlertResult`
- **Notifications**: `DocumentChangedNotification`, `ImportCompleteNotification`, `LinkClickedParams`

### Error Handling

```csharp
throw new BridgeException(JsonRpcErrorCodes.InvalidParams, "File not found", new { path });
```

Unhandled exceptions are wrapped as JSON-RPC Internal Errors.

## Architecture Patterns

### Auto-Save Flow

1. Host timer fires, calls `host.Document.RequestSave()`
2. JS receives `document/requestSave` notification
3. JS handler calls `client.document.save(content)`
4. C# `OnSave` handler saves to disk

### External Change Flow

1. Host file watcher detects change
2. Host calls `host.Document.NotifyExternalChange()`
3. JS receives notification, calls `client.document.load()` to refresh

## Existing Editors

- **Markdown**: `Modules/Celbridge.Markdown/Views/MarkdownDocumentView.xaml.cs`
- **Spreadsheet**: `Modules/Celbridge.Spreadsheet/Views/SpreadsheetDocumentView.xaml.cs`
- **Screenplay**: `Modules/Celbridge.Screenplay/Views/SceneDocumentView.xaml.cs`

## Testing

### JavaScript Tests

```bash
cd Core/Celbridge.UserInterface/Web
npm ci
npm test
```

### C# Tests

`Celbridge.Tests/Helpers/CelbridgeHostTests.cs` using `MockWebViewMessageChannel`.
