# WebView Bridge: JSON-RPC Communication Layer

## Overview

This document outlines the design for a standardized communication layer between WebView2-hosted editors and the Celbridge host application. The bridge replaces ad-hoc message passing with a robust JSON-RPC based API.

## How to Use This Document

This is a **living design document** that serves as both specification and implementation tracker.

**For the AI assistant (Copilot):** When the user says "Implement the next phase", you should:
1. Read this document to understand the current status and next phase
2. Review the "Key Files" listed in Current Status for context
3. Implement the phase according to the tasks listed
4. Update the phase status from ⬜ to ✅ when complete
5. Record any deviations or decisions in [WebViewBridgeNotes.md](WebViewBridgeNotes.md)
6. Update the "Current Status" section using the Phase Context Templates at the end
7. If the design changes during implementation, update the relevant sections in this doc

**Status indicators:**
- ⬜ Not started
- 🔄 In progress  
- ✅ Complete

**Deviation policy:** Minor implementation details can be updated freely. Significant deviations from Requirements should be discussed with the user first.

**After completing a phase:**
1. Update the phase status indicator (⬜ → ✅)
2. Replace the "Current Status" section with the next phase's context (see templates at end)
3. Add notes to [WebViewBridgeNotes.md](WebViewBridgeNotes.md)

## Current Status

**Next Phase:** Phase 2 - Markdown Editor Migration

**Key Files to Review:**
- `Modules/Celbridge.Markdown/Views/MarkdownDocumentView.xaml.cs` - C# side to migrate
- `Modules/Celbridge.Markdown/Web/markdown.js` - JS side to migrate
- `Core/Celbridge.UserInterface/Helpers/WebViewBridge.cs` - Bridge API (from Phase 1)
- `Core/Celbridge.UserInterface/WebAssets/webview-bridge.js` - JS bridge (from Phase 1)

**Verification:**
- [ ] `dotnet build` succeeds
- [ ] Create new markdown document → editor loads, can type
- [ ] Open existing markdown document → content displays correctly
- [ ] Edit and wait for auto-save → file saved to disk
- [ ] Insert image via toolbar → image picker works
- [ ] External file change (clean) → reloads without prompt
- [ ] External file change (dirty) → conflict dialog appears

## Rationale

### Current Problems

The existing WebView2 editors (Markdown, Spreadsheet, Screenplay, WebApp) each implement their own message-passing patterns:

1. **Race Conditions**: No coordination between "editor ready" and "content loaded" states. The recent Markdown editor bug where documents were overwritten with empty content stemmed from this.

2. **No Request/Response Correlation**: Messages are fire-and-forget. The sender has no way to know if/when a response arrives, leading to scattered guard flags and hope-based programming.

3. **Duplicated Patterns**: Each editor reinvents initialization sequences, save flows, dialog interactions, and error handling.

4. **Fragile State Management**: Guard flags like `_isContentLoaded`, `isLoadingContent`, and `isDocumentLoaded` are scattered across C# and JavaScript to paper over timing issues.

5. **Hard to Test**: Message timing dependencies make unit testing difficult.

### Proposed Solution

A shared JSON-RPC bridge that provides:
- Promise-based async/await API for JavaScript
- Automatic request/response correlation via message IDs
- Standardized initialization handshake
- Common API for document operations, dialogs, and host services
- Clear separation between requests (client→host) and notifications (bidirectional)

## Requirements

### Functional Requirements

1. **JSON-RPC 2.0 Compliance**: Follow the standard specification for request/response/notification semantics.

2. **Initialization Handshake**: JavaScript calls `await bridge.initialize()` which resolves only after the host confirms the editor is fully initialized with content.

3. **Document Operations**:
   - `document.load()` - Returns current content from disk (used for external reload)
   - `document.save(content)` - Saves content, returns success/failure
   - `document.getMetadata()` - Returns file path, resource key, etc.

4. **Dialog Operations**:
   - `dialog.pickImage(extensions)` - Opens image picker, returns selected path
   - `dialog.pickFile(extensions)` - Opens file picker, returns selected path
   - `dialog.alert(title, message)` - Shows alert dialog

5. **Events** (typed API for notifications):
   - **Outbound** (client → host): `document.notifyChanged()` - signals content modification
   - **Inbound** (host → client): `document.onExternalChange()`, `theme.onChanged()`, `localization.onUpdated()`

6. **External Reload Flow**: When the host detects external file changes:
   - The host tracks dirty state via incoming `document/changed` notifications
   - **If clean**: Host sends `document/externalChange` notification; JS calls `document.load()` to fetch fresh content, preserving editor state (caret position, scroll, selection)
   - **If dirty**: A conflict exists between local edits and external changes. The document must remain consistent with disk (auto-save model), so the user must resolve the conflict immediately. Host shows a dialog with options:
     - **Reload**: Discard local changes, load external version (disk wins)
     - **Overwrite**: Save local changes to disk, discarding the external change (local wins)
     - **Save As**: Save local changes to a new file, then reload the original with external version (both preserved)
   - There is no "keep local changes without saving" option—this would violate the auto-save consistency model.
   - After resolution, host sends `document/externalChange` (for Reload/Save As) or the document is already in sync (for Overwrite).

7. **Error Handling**: Rejected promises with structured error information (code, message, data).

8. **Timeout Handling**: Requests that don't receive responses within a configurable timeout should reject.

9. **Exception Propagation (C#)**: The `WebViewBridge.cs` implementation must wrap all handler invocations in `try/catch`. Any unhandled C# exception (e.g., `FileNotFoundException`, `InvalidOperationException`) must be caught and serialized into a valid JSON-RPC Error Response (code `-32603` "Internal Error"). This prevents JavaScript `await` calls from hanging indefinitely if the host throws during request handling.

10. **Keyboard Shortcuts**: Latency-sensitive operations like keyboard shortcuts must be sent as **Notifications** (one-way, no ID) rather than Requests. They should not be queued behind long-running file operations in the promise chain.

### Non-Functional Requirements

1. **Minimal Dependencies**: Pure JavaScript module, no external libraries required.

2. **Type Safety via JSDoc**: Use JSDoc annotations in the JavaScript source for IDE intellisense. No TypeScript compilation—the `.js` file ships as-is. This is sufficient for AI-assisted development where code review matters more than autocomplete.

3. **Modularity Preserved**: The shared bridge lives in `Core/Celbridge.UserInterface/WebAssets` alongside `celbridge-localization.js`. This allows use by Home Page, Community Page, and other non-workspace contexts. Editor-specific JS (markdown.js, spreadsheet.js) stays in their respective modules.

4. **Backward Compatibility**: Existing editors can migrate incrementally; old message patterns continue to work during transition.

5. **Performance**: Negligible overhead; JSON serialization is already the baseline.

## Architecture

### Components

```
┌─────────────────────────────────────────────────────────────┐
│                     JavaScript (WebView2)                    │
│  ┌─────────────────────────────────────────────────────┐    │
│  │                    webview-bridge.js                 │    │
│  │  - JSON-RPC request/response handling               │    │
│  │  - Promise management for pending requests          │    │
│  │  - Notification dispatch                            │    │
│  │  - Typed API surface (document, dialog, etc.)       │    │
│  │  - Debug logging (configurable)                     │    │
│  └─────────────────────────────────────────────────────┘    │
│                            │                                 │
│                   postMessage / onMessage                    │
└────────────────────────────┼────────────────────────────────┘
                             │
┌────────────────────────────┼────────────────────────────────┐
│                     C# Host Application                      │
│                            │                                 │
│  ┌─────────────────────────────────────────────────────┐    │
│  │                   WebViewBridge.cs                   │    │
│  │  - JSON-RPC message parsing                         │    │
│  │  - Method dispatch to registered handlers           │    │
│  │  - Response serialization                           │    │
│  │  - Notification sending                             │    │
│  └─────────────────────────────────────────────────────┘    │
│                            │                                 │
│  ┌─────────────────────────────────────────────────────┐    │
│  │              Editor-Specific Handlers                │    │
│  │  - MarkdownBridgeHandlers.cs                        │    │
│  │  - SpreadsheetBridgeHandlers.cs                     │    │
│  │  - ScreenplayBridgeHandlers.cs                      │    │
│  │  - WebAppBridgeHandlers.cs                          │    │
│  └─────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

### Message Format

**Request (Client → Host):**
```json
{
  "jsonrpc": "2.0",
  "method": "document/load",
  "params": { "includeMetadata": true },
  "id": 1
}
```

**Response (Host → Client):**
```json
{
  "jsonrpc": "2.0",
  "result": { "content": "# Hello", "path": "/docs/readme.md" },
  "id": 1
}
```

**Error Response:**
```json
{
  "jsonrpc": "2.0",
  "error": { "code": -32600, "message": "File not found" },
  "id": 1
}
```

**Notification (no `id` field):**
```json
{
  "jsonrpc": "2.0",
  "method": "document/externalChange",
  "params": {}
}
```

### Protocol Versioning

The `bridge/initialize` request includes a `protocolVersion` field. The host validates compatibility and returns an error if the JavaScript bridge is outdated (e.g., cached old version). Initial version: `"1.0"`.

### Initialization Sequence

```
C# Host                          JavaScript
    │                                 │
    │──── Navigate to editor ────────>│
    │                                 │
    │                                 │ Editor initializes (hidden)
    │                                 │
    │<─── bridge/initialize request ──│ (includes protocolVersion)
    │                                 │
    │     Validate protocol version   │
    │     Load content from file      │
    │     Send localization           │
    │     Prepare metadata            │
    │                                 │
    │──── bridge/initialize response ─>│ (includes content + config)
    │                                 │
    │                                 │ Set content, show editor
    │                                 │ initialize() promise resolves
    │                                 │
    │<─── document/changed ───────────│ (user edits)
    │                                 │
    │──── document/save request ─────>│ (auto-save timer)
    │                                 │
    │<─── document/save response ─────│
```

## Implementation Plan

### Phase 1: Core Infrastructure ✅

**Deliverables:**
- `Core/Celbridge.UserInterface/` - Add to existing project:
  - `WebAssets/webview-bridge.js` - Shared JavaScript module with JSDoc annotations
  - `WebAssets/webview-bridge.test.js` - Vitest unit tests
  - `WebAssets/package.json` - Minimal npm config for Vitest
  - `WebAssets/vitest.config.js` - Vitest configuration
  - `Helpers/WebViewBridge.cs` - Core C# bridge class with typed handler registration
  - `Helpers/IWebViewMessageChannel.cs` - Interface abstracting WebView2 (for testability)
- Typed request/response records: `InitializeParams`, `InitializeResult`, `LoadResult`, `SaveParams`, etc.
- Debug logging infrastructure
- Deprecation of `WebView2Messenger.cs` (keep for backward compatibility during migration)

**Tasks:**
1. Add WebView bridge files to `Celbridge.UserInterface` (no new project needed)
2. Implement JSON-RPC message parsing and serialization (C#)
3. Implement typed handler registration (`_bridge.Document.OnSave(...)`, etc.)
4. Implement request/response correlation with pending request map (JS)
5. Implement timeout handling for requests (JS)
6. Implement notification dispatch (both sides)
7. Create shared module hosting via virtual host mapping
8. Implement debug logging (console + optional C# forwarding)
9. Write unit tests (C# xUnit + JS Vitest)
10. Add JS test step to CI workflow

### Phase 2: Markdown Editor Migration ⬜

**Deliverables:**
- `MarkdownBridgeHandlers.cs` - Markdown-specific method handlers
- Updated `markdown.js` using bridge API
- Removal of ad-hoc message handling and guard flags

**Tasks:**
1. Register handlers for `bridge/initialize`, `document/save`, `dialog/pickImage`, `dialog/pickFile`
2. Implement `bridge/initialize` handler that bundles content + localization + config
3. Refactor `markdown.js` to use `await bridge.initialize()` for initialization
4. Replace `request-save`/`save-response` pattern with `await bridge.document.save()`
5. Replace image/link picker messages with `await bridge.dialog.pickImage()`
6. Remove `_isContentLoaded`, `isDocumentLoaded`, `isLoadingContent` guards
7. Test thoroughly: new document, existing document, reload, external changes

### Phase 3: Spreadsheet Editor Migration ⬜

**Deliverables:**
- `SpreadsheetBridgeHandlers.cs`
- Updated SpreadJS editor JavaScript

**Tasks:**
1. Analyze current spreadsheet editor message patterns (likely the most complex)
2. Map SpreadJS-specific operations to bridge API
3. Handle spreadsheet-specific data formats (cell data, formulas, styling)
4. Migrate JavaScript to use bridge
5. Test all spreadsheet workflows: editing, formulas, formatting, large datasets

**Note:** The Spreadsheet editor is likely the most complex due to SpreadJS integration and the variety of data operations. This migration may surface additional bridge requirements.

### Phase 4: Screenplay Editor Migration ⬜

**Deliverables:**
- `ScreenplayBridgeHandlers.cs`
- Updated screenplay editor JavaScript

**Tasks:**
1. Analyze current screenplay editor message patterns
2. Map existing functionality to bridge API
3. Implement any screenplay-specific methods
4. Migrate JavaScript to use bridge
5. Test all screenplay editor workflows

### Phase 5: WebApp Viewer Migration ⬜

**Deliverables:**
- `WebAppBridgeHandlers.cs`
- Updated WebApp viewer JavaScript

**Tasks:**
1. Analyze current WebApp viewer requirements
2. Implement necessary bridge methods
3. Migrate to bridge API
4. Test navigation, content loading, etc.

### Phase 6: Cleanup and Documentation ⬜

**Deliverables:**
- Remove legacy message handling code
- API documentation
- Developer guide for creating new WebView editors

**Tasks:**
1. Remove `WebView2Messenger` and ad-hoc message types
2. Document bridge API methods
3. Create template/guide for new editors
4. Update any affected tests

## Testing Strategy

### Problem
WebView2 is Windows-only, but our CI runs on Linux. We need to test the bridge without the actual WebView2 runtime.

### Approach

**C# Unit Tests (xUnit, runs on Linux CI):**
- `WebViewBridge` depends on `IWebViewMessageChannel` (abstracts `PostWebMessageAsJson` and message events)
- Tests inject a mock channel that captures outgoing messages and can simulate incoming messages
- Covers: JSON-RPC parsing, handler dispatch, response correlation, error handling, timeouts

**JavaScript Unit Tests (Vitest, runs on Linux CI):**
- Only the shared `webview-bridge.js` is tested—not editor-specific JS
- Bridge constructor accepts optional `postMessage`/`onMessage` hooks for testing
- In production, defaults to `window.chrome.webview.postMessage` and `addEventListener`
- Tests verify: request serialization, response handling, timeout behavior, notification dispatch

**Editor-Specific JS (markdown.js, etc.):**
- Not unit tested—tested manually via the editor
- Rationale: Editor JS is tightly coupled to its C# counterpart and third-party libraries (TipTap, SpreadJS); automated tests would be brittle and low-value
- If pain emerges, can add tests later

**Integration Tests (Windows CI or manual):**
- Actual WebView2 round-trips, marked with `[Trait("Category", "WebView2")]`
- Skipped on Linux runners via CI configuration
- Primarily smoke tests; correctness proven by unit tests

### What We Don't Test
- WebView2 itself (Microsoft's responsibility)
- Browser JavaScript engine behavior
- Editor-specific JS integration code (manual testing)

## JavaScript Tooling

### Design Principles

1. **Minimal footprint**: Only what's needed to test the shared bridge
2. **Preserve modularity**: Editor JS stays in its respective module
3. **No .csproj changes**: JS tooling is separate from .NET build
4. **No TypeScript compilation**: JSDoc provides sufficient type safety for AI-assisted development

### Structure

```
Core/
  Celbridge.UserInterface/                # Existing project
    WebAssets/
      celbridge-localization.js           # Existing shared localization
      webview-bridge.js                   # NEW - Shared bridge module
      webview-bridge.test.js              # NEW - Bridge tests
      package.json                        # NEW - Minimal: vitest only
      vitest.config.js                    # NEW
    Helpers/
      WebView2Helper.cs                   # Existing
      WebView2Messenger.cs                # Existing (deprecated after migration)
      WebViewBridge.cs                    # NEW - C# bridge implementation
      IWebViewMessageChannel.cs           # NEW

Modules/
  Celbridge.Markdown/
    Web/
      markdown.js                         # Stays here (no tests)
  Celbridge.Spreadsheet/
    Web/
      spreadsheet.js                      # Stays here (no tests)
```

### package.json (Minimal)

```json
{
  "name": "celbridge-webview-bridge",
  "private": true,
  "type": "module",
  "scripts": {
    "test": "vitest run",
    "test:watch": "vitest"
  },
  "devDependencies": {
    "vitest": "^2.0.0"
  }
}
```

### CI Integration

Single additional step in GitHub Actions workflow:

```yaml
- name: Test WebView Bridge (JS)
  working-directory: Core/Celbridge.UserInterface/WebAssets
  run: |
    npm ci
    npm test
```

**Impact**: ~30 seconds added to CI. No changes to .csproj files or .NET build.

### Why Not TypeScript?

TypeScript's main benefits (autocomplete, immediate error feedback) are less valuable when AI generates most of the JavaScript code. JSDoc provides:
- IDE intellisense for code review
- Optional CI type checking (`tsc --noEmit` if desired later)
- No build step—`.js` files ship as-is

## API Reference (Initial)

### JavaScript API

```javascript
import { bridge } from 'https://shared.celbridge/webview-bridge.js';

// Initialization - must be called before any other operations
// Returns content + config; resolves when host confirms ready
const { content, metadata, localization } = await bridge.initialize();

// Document operations (requests → host)
const { content } = await bridge.document.load();  // Fetch current content from disk
await bridge.document.save(markdownContent);
const metadata = await bridge.document.getMetadata();

// Dialog operations (requests → host)
const imagePath = await bridge.dialog.pickImage(['.png', '.jpg']);
const filePath = await bridge.dialog.pickFile(['.md', '.txt']);
await bridge.dialog.alert('Title', 'Message');

// Outbound notifications (client → host, no response)
bridge.document.notifyChanged();  // Signals content has been modified

// Inbound event handlers (host → client, typed subscriptions)
bridge.document.onExternalChange(async () => { 
    // File changed outside editor - host has already handled dirty state confirmation
    // If we receive this notification, we should reload.
    const scrollPos = editor.getScrollPosition();
    const caretPos = editor.getCaretPosition();

    const { content } = await bridge.document.load();
    editor.setContent(content);
    editor.markClean(); // Reset dirty state after reload

    editor.setScrollPosition(scrollPos);
    editor.setCaretPosition(caretPos);
});
bridge.theme.onChanged((theme) => { 
    // theme.name: string, theme.isDark: boolean
});
bridge.localization.onUpdated((strings) => {
    // Refreshed localization strings
});
```

### C# Registration

```csharp
public class MarkdownDocumentView : WebView2DocumentView
{
    private WebViewBridge _bridge;

    protected override async Task InitializeAsync()
    {
        // IWebViewMessageChannel abstracts WebView2 for testability
        var channel = new WebView2MessageChannel(WebView.CoreWebView2);
        _bridge = new WebViewBridge(channel);

        // Type-safe handler registration using generics.
        // Signature: RegisterHandler<TParams, TResult>(string method, Func<TParams, Task<TResult>> handler)
        // This enforces compile-time type safety between request params and result types.
        _bridge.OnInitialize(HandleInitialize);
        _bridge.Document.OnLoad(HandleDocumentLoad);
        _bridge.Document.OnSave(HandleDocumentSave);
        _bridge.Document.OnChanged(OnDocumentChanged);  // Track dirty state
        _bridge.Dialog.OnPickImage(HandlePickImage);
        _bridge.Dialog.OnPickFile(HandlePickFile);

        await _bridge.StartAsync();
    }

    private async Task<InitializeResult> HandleInitialize(InitializeParams request)
    {
        // Validate protocol version
        if (request.ProtocolVersion != "1.0")
        {
            throw new BridgeException(ErrorCodes.InvalidVersion, "Unsupported protocol version");
        }

        var content = await File.ReadAllTextAsync(FilePath);
        return new InitializeResult(content, GetMetadata(), GetStrings());
    }

    private async Task<LoadResult> HandleDocumentLoad(LoadParams request)
    {
        // All handlers are wrapped in try/catch by WebViewBridge.
        // Exceptions become JSON-RPC error responses (code -32603).
        var content = await File.ReadAllTextAsync(FilePath);
        return new LoadResult(content);
    }

    private async Task OnExternalFileChange()
    {
        // File watcher detected change - check dirty state before notifying JS
        if (_isDirty)
        {
            // Conflict: local edits vs external changes
            // Document must stay consistent with disk (auto-save model)
            var resolution = await _dialogService.ShowConflictDialogAsync(
                "External Change Detected",
                "This file has been modified externally while you have unsaved changes.",
                ConflictResolution.Reload,    // Discard local, load external
                ConflictResolution.Overwrite, // Save local, discard external  
                ConflictResolution.SaveAs);   // Save local elsewhere, load external

            switch (resolution)
            {
                case ConflictResolution.Reload:
                    // Discard local changes, notify JS to reload
                    _isDirty = false;
                    _bridge.Document.NotifyExternalChange();
                    break;

                case ConflictResolution.Overwrite:
                    // Save local content to disk (overwrites external change)
                    await SaveDocumentAsync();
                    _isDirty = false;
                    // No reload needed - local version is now the disk version
                    break;

                case ConflictResolution.SaveAs:
                    // Save local to new location, then reload original
                    await SaveDocumentAsAsync();
                    _isDirty = false;
                    _bridge.Document.NotifyExternalChange();
                    break;
            }
            return;
        }

        // No conflict - notify JS to reload
        _bridge.Document.NotifyExternalChange();
    }

    private void OnDocumentChanged()
    {
        // JS sends this notification whenever content is modified
        _isDirty = true;
    }
}
```

## Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| Migration breaks existing functionality | Incremental migration with feature flags; keep old code until verified |
| Performance overhead | JSON-RPC adds minimal overhead; measure before/after |
| Complexity of bridge implementation | Follow JSON-RPC spec strictly; comprehensive tests |
| Timeout values too aggressive/lenient | Make configurable; start conservative (30s) |
| Data loss on external file changes | Dirty state checking before reload; user confirmation for conflicts |

## Success Criteria

1. All editors work identically to current behavior (no regressions)
2. Race condition bugs are structurally impossible (no guard flags needed)
3. New editor implementation requires <50% of current boilerplate
4. All WebView editors use the same bridge module
5. Clear error messages when bridge calls fail
6. Debug logging provides clear visibility into message flow during development

## Debug Logging

Debug logging is built into the bridge from the start to aid development and troubleshooting:

**JavaScript side:**
```javascript
// Enable verbose logging
bridge.setLogLevel('debug'); // 'none' | 'error' | 'warn' | 'debug'

// Logs show:
// [WebViewBridge] → request #1: document/load {...}
// [WebViewBridge] ← response #1: success (42ms)
// [WebViewBridge] ← notification: theme/changed {...}
```

**C# side:**
```csharp
// Uses standard ILogger
// Configure via appsettings or DI
_bridge.EnableDetailedLogging = true;
```

Logs include:
- Request/response correlation with timing
- Notification dispatch
- Errors with full context
- Timeout warnings

## Future Considerations

- **Bidirectional streaming**: For large documents, consider chunked transfer. However, prefer using the Virtual Host mapping (`https://project.celbridge/...`) which allows the WebView to stream large assets directly from disk using the browser's native optimized network stack. Only implement custom chunking if specific performance metrics (e.g., >10MB text files) prove problematic during testing.
- **Cancellation**: Add support for cancelling in-flight requests
- **Batching**: Multiple requests in a single message for performance
- **Visual inspector**: Browser DevTools-style panel for message inspection

---

## Implementation Notes

See **[WebViewBridgeNotes.md](WebViewBridgeNotes.md)** for detailed implementation notes, decisions, and deviations recorded during each phase.

---

## Phase Context Templates

When completing a phase, copy the appropriate template below into the "Current Status" section:

<details>
<summary>Phase 2 Context (click to expand)</summary>

```markdown
**Next Phase:** Phase 2 - Markdown Editor Migration

**Key Files to Review:**
- `Modules/Celbridge.Markdown/Views/MarkdownDocumentView.xaml.cs` - C# side to migrate
- `Modules/Celbridge.Markdown/Web/markdown.js` - JS side to migrate
- `Core/Celbridge.UserInterface/Helpers/WebViewBridge.cs` - Bridge API (from Phase 1)
- `Core/Celbridge.UserInterface/WebAssets/webview-bridge.js` - JS bridge (from Phase 1)

**Verification:**
- [ ] `dotnet build` succeeds
- [ ] Create new markdown document → editor loads, can type
- [ ] Open existing markdown document → content displays correctly
- [ ] Edit and wait for auto-save → file saved to disk
- [ ] Insert image via toolbar → image picker works
- [ ] External file change (clean) → reloads without prompt
- [ ] External file change (dirty) → conflict dialog appears
```

</details>

<details>
<summary>Phase 3 Context (click to expand)</summary>

```markdown
**Next Phase:** Phase 3 - Spreadsheet Editor Migration

**Key Files to Review:**
- `Modules/Celbridge.Spreadsheet/Views/SpreadsheetDocumentView.xaml.cs`
- `Modules/Celbridge.Spreadsheet/Web/` - SpreadJS integration
- `Core/Celbridge.UserInterface/Helpers/WebViewBridge.cs`

**Verification:**
- [ ] `dotnet build` succeeds
- [ ] Create/open spreadsheet → displays correctly
- [ ] Edit cells, formulas → auto-save works
- [ ] Large dataset performance acceptable
```

</details>

<details>
<summary>Phase 4 Context (click to expand)</summary>

```markdown
**Next Phase:** Phase 4 - Screenplay Editor Migration

**Key Files to Review:**
- `Modules/Celbridge.Screenplay/Views/ScreenplayDocumentView.xaml.cs`
- `Modules/Celbridge.Screenplay/Web/`
- `Core/Celbridge.UserInterface/Helpers/WebViewBridge.cs`

**Verification:**
- [ ] `dotnet build` succeeds
- [ ] Create/open screenplay → displays correctly
- [ ] Edit and auto-save works
```

</details>

<details>
<summary>Phase 5 Context (click to expand)</summary>

```markdown
**Next Phase:** Phase 5 - WebApp Viewer Migration

**Key Files to Review:**
- `Modules/Celbridge.WebApp/Views/WebAppDocumentView.xaml.cs`
- `Modules/Celbridge.WebApp/Web/`
- `Core/Celbridge.UserInterface/Helpers/WebViewBridge.cs`

**Verification:**
- [ ] `dotnet build` succeeds
- [ ] WebApp viewer loads content
- [ ] Navigation works
```

</details>

<details>
<summary>Phase 6 Context (click to expand)</summary>

```markdown
**Next Phase:** Phase 6 - Cleanup and Documentation

**Key Files to Review:**
- `Core/Celbridge.UserInterface/Helpers/WebView2Messenger.cs` - To be removed
- All migrated editor views - Verify no legacy references remain

**Verification:**
- [ ] `dotnet build` succeeds (no references to removed code)
- [ ] All editors still work
- [ ] Documentation complete
```

</details>
