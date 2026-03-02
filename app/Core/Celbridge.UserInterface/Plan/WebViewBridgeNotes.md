# WebView Bridge: Implementation Notes

This companion document to `WebViewBridge.md` records decisions, deviations, and learnings during implementation.

**For the AI assistant:** After completing each phase, add a brief summary here documenting:
- Any deviations from the original plan
- Design decisions made during implementation
- Issues encountered and how they were resolved
- API changes that affect other phases

Keep notes concise—this file may be referenced in future sessions.

---

## Phase 1: Core Infrastructure

**Completed:** Yes

**Files Created:**
- `Core/Celbridge.UserInterface/Helpers/IWebViewMessageChannel.cs` - Interface for WebView2 abstraction
- `Core/Celbridge.UserInterface/Helpers/WebViewBridgeContracts.cs` - Typed records for all request/response types
- `Core/Celbridge.UserInterface/Helpers/WebViewBridge.cs` - Core C# bridge with typed handler registration
- `Core/Celbridge.UserInterface/Web/webview-bridge.js` - JavaScript bridge module with JSDoc
- `Core/Celbridge.UserInterface/Web/webview-bridge.test.js` - 18 Vitest unit tests
- `Core/Celbridge.UserInterface/Web/package.json` - npm config for Vitest
- `Core/Celbridge.UserInterface/Web/vitest.config.js` - Vitest configuration
- `Celbridge.Tests/Helpers/WebViewBridgeTests.cs` - 13 NUnit unit tests

**Deviations from Plan:**
- Task 10 (Add JS test step to CI workflow): Skipped as noted in the plan. No CI workflow exists yet.
- JS singleton export changed from `export const bridge = new WebViewBridge()` to `export function getBridge()` to avoid `window is not defined` errors in Node.js test environment. The lazy initialization pattern is actually cleaner.

**Design Decisions:**
1. Used NUnit instead of xUnit for C# tests (matches existing test project setup).
2. `MockWebViewMessageChannel` helper class created to simulate WebView2 messages in tests.
3. Both C# and JS sides handle malformed JSON gracefully without throwing.
4. Request timeout default is 30 seconds in production, configurable via constructor.
5. `WebView2Messenger.cs` marked with `[Obsolete]` attribute but not modified otherwise.

**Test Results:**
- C# tests: 13 passed
- JS tests: 18 passed
- Build: Successful

---

## Phase 2: Markdown Editor Migration

**Completed:** Yes

**Files Created:**
- `Core/Celbridge.UserInterface/Helpers/WebView2MessageChannel.cs` - Production implementation of IWebViewMessageChannel
- `Core/Celbridge.UserInterface/Helpers/WebViewLocalizationHelper.cs` - Helper to extract localization strings for WebView editors

**Files Modified:**
- `Core/Celbridge.UserInterface/Helpers/WebViewBridge.cs` - Added Theme, Localization handlers; Document.RequestSave(), Document.OnLinkClicked()
- `Core/Celbridge.UserInterface/Helpers/WebViewBridgeContracts.cs` - Added LinkClickedParams, MarkdownEditorConfig
- `Core/Celbridge.UserInterface/Web/webview-bridge.js` - Added document.onRequestSave() event
- `Modules/Celbridge.Markdown/Views/MarkdownDocumentView.xaml.cs` - Full migration to WebViewBridge
- `Modules/Celbridge.Markdown/Web/Markdown/markdown.js` - Full migration to bridge API
- `Modules/Celbridge.Markdown/Web/Markdown/markdown-image-popover.js` - Use bridge.dialog.pickImage()
- `Modules/Celbridge.Markdown/Web/Markdown/markdown-link-popover.js` - Use bridge.dialog.pickFile(), bridge notifications

**Deviations from Plan:**
1. **Save flow**: The plan described JS-initiated saves, but the existing auto-save infrastructure in C# relies on timer-based saves. Added `document/requestSave` notification (host → client) so C# can request a save, and JS responds by calling `bridge.document.save(content)`. This preserves the existing auto-save behavior while using the bridge.

2. **Link clicked handling**: Added `link/clicked` notification for handling link clicks, registered via `Document.OnLinkClicked()`.

3. **No separate MarkdownBridgeHandlers.cs**: Handlers are defined inline in `MarkdownDocumentView.xaml.cs` rather than a separate file. This keeps the view self-contained and avoids additional files for minimal code.

**Design Decisions:**
1. `WebView2MessageChannel.Detach()` method added to cleanly unsubscribe from WebView2 events during disposal.
2. Localization strings are now bundled in the `InitializeResult` response, eliminating the need for a separate `set-localization` message.
3. Theme changes are pushed to JS via `theme/changed` notification when the host detects a theme change.
4. The `_isDirty` flag in the view tracks whether JS has unsaved changes (via `document/changed` notifications).
5. Removed guard flags (`_isContentLoaded`, `isLoadingContent`, `isDocumentLoaded`) - the bridge initialization handshake handles timing.

**Test Results:**
- Build: Successful

**Manual Testing Required:**
- [ ] Create new markdown document → editor loads, can type
- [ ] Open existing markdown document → content displays correctly
- [ ] Edit and wait for auto-save → file saved to disk
- [ ] Insert image via toolbar → image picker works
- [ ] Insert link via toolbar → file picker works
- [ ] Click link (Ctrl+click) → opens document/browser
- [ ] External file change (clean) → reloads without prompt
- [ ] External file change (dirty) → conflict dialog appears
- [ ] Theme change → editor updates

---

## Phase 3: Spreadsheet Editor Migration

**Completed:** Yes

**Files Modified:**
- `Core/Celbridge.UserInterface/Helpers/WebViewBridge.cs` - Added Document.OnSaveBinary(), Document.OnLoadBinary(), Document.OnImportComplete()
- `Core/Celbridge.UserInterface/Helpers/WebViewBridgeTypes.cs` - Added SaveBinaryParams, SaveBinaryResult, LoadBinaryResult, ImportCompleteNotification
- `Core/Celbridge.UserInterface/Web/webview-bridge.js` - Added document.saveBinary(), document.loadBinary(), document.notifyImportComplete()
- `Modules/Celbridge.Spreadsheet/Views/SpreadsheetDocumentView.xaml.cs` - Full migration to WebViewBridge
- `Modules/Celbridge.Spreadsheet/Web/SpreadJS/index.html` - Full migration to bridge API (ES module)

**Deviations from Plan:**
1. **Binary content via content field**: Rather than creating a separate binary initialization flow, the spreadsheet uses the existing `InitializeResult.Content` field to pass base64-encoded Excel data. This keeps the API simple while supporting both text and binary content.

2. **Import complete notification**: Added `import/complete` notification (JS to C#) to signal when SpreadJS has finished importing data. This replaces the old `import_complete` message and provides structured success/error information.

3. **No separate SpreadsheetBridgeHandlers.cs**: Following the Markdown pattern, handlers are defined inline in `SpreadsheetDocumentView.xaml.cs` rather than a separate file.

**Design Decisions:**
1. **Binary document API**: Created separate `document/saveBinary` and `document/loadBinary` methods that use base64 encoding. This keeps the API explicit about data types rather than overloading the text-based methods.

2. **Import state tracking**: The `_isImportInProgress` and `_hasPendingImport` flags are preserved from the original implementation to handle race conditions when multiple file changes occur during import.

3. **Removed legacy message handlers**: The old `editor_ready`, `request_save`, `data_changed`, `import_complete`, and `load_excel_data` message patterns are all replaced by the bridge API.

4. **ES module conversion**: Changed the script block in index.html from inline script to ES module (`type="module"`) to import the bridge.

5. **Shared assets mapping**: Added `WebView2Helper.MapSharedAssets()` call to map the shared assets virtual host (required for importing the bridge JS module).

**Changes to Bridge API:**
- `WebViewBridge.Document.OnSaveBinary()` - Register handler for binary save requests
- `WebViewBridge.Document.OnLoadBinary()` - Register handler for binary load requests  
- `WebViewBridge.Document.OnImportComplete()` - Register handler for import completion notifications
- JS `bridge.document.saveBinary(base64)` - Save binary content
- JS `bridge.document.loadBinary()` - Load binary content from disk
- JS `bridge.document.notifyImportComplete(success, error)` - Notify import completion

**Test Results:**
- Build: Successful

**Manual Testing Required:**
- [ ] Create/open spreadsheet - displays correctly
- [ ] Edit cells, formulas - changes detected
- [ ] Wait for auto-save - file saved to disk
- [ ] External file change - reloads spreadsheet
- [ ] Large dataset performance acceptable

---

## Phase 4: Screenplay Editor Migration

**Completed:** Yes

**Files Created:**
- `Modules/Celbridge.Screenplay/Web/Screenplay/index.html` - Static HTML viewer with bridge integration

**Files Modified:**
- `Modules/Celbridge.Screenplay/Celbridge.Screenplay.csproj` - Added Web content include and UserInterface project reference
- `Modules/Celbridge.Screenplay/Views/SceneDocumentView.xaml.cs` - Full migration to WebViewBridge
- `Modules/Celbridge.Screenplay/ViewModels/SceneDocumentViewModel.cs` - Changed to generate body content only (not full HTML document)

**Deviations from Plan:**
1. **Read-only viewer pattern**: The Screenplay editor is fundamentally different from Markdown/Spreadsheet - it's a read-only viewer that generates HTML on the C# side. The bridge is used for initialization and theme changes only, not for editing/saving.

2. **HTML content generation**: Moved CSS styling to the static `index.html` file. The ViewModel now generates only the body content (screenplay/page div structure), which is injected via the `content` field in `InitializeResult`.

3. **Theme changes via notification**: Instead of regenerating the entire HTML and re-navigating (old approach), theme changes now send a `theme/changed` notification. The JS applies the theme by changing the body class.

4. **Content updates via external change**: When scene content is updated (via `SceneContentUpdatedMessage`), the view notifies JS via `document/externalChange`. JS then calls `document.load()` to fetch the updated content and re-renders.

**Design Decisions:**
1. **Static HTML with injected content**: The HTML structure and CSS are in the static `index.html` file. The C# side generates only the screenplay content, which is injected into the `#screenplay-container` div. This separates concerns and makes theme switching efficient.

2. **No save functionality**: Since this is a read-only viewer, no save handlers are registered. The bridge only handles `initialize` and `load` requests.

3. **Virtual host naming**: Used `screenplay.celbridge` as the virtual host name, following the pattern established by `spreadjs.celbridge`.

**Test Results:**
- Build: Successful

**Manual Testing Required:**
- [ ] Create/open screenplay - displays correctly
- [ ] Scene content updates - viewer refreshes
- [ ] Theme change (light/dark) - applies without page reload

---

## Phase 5: WebApp Viewer Migration

**Completed:** Yes

**Files Modified:** None

**Analysis:**
The WebApp viewer is fundamentally different from the other editors (Markdown, Spreadsheet, Screenplay). It is a **URL browser** that:
1. Reads a `.webapp` JSON file to extract a URL
2. Navigates directly to external URLs using WebView2's native `CoreWebView2.Navigate()` API
3. Uses WebView2's native APIs for navigation (GoBack, GoForward, Reload)
4. Handles downloads through WebView2's native download events
5. Does NOT have any custom JavaScript layer or HTML page to communicate with

**Conclusion:**
No bridge migration is needed for the WebApp viewer because there is no custom JavaScript to bridge. The viewer operates correctly using WebView2's native APIs. The plan's deliverables (`WebAppBridgeHandlers.cs`, "Updated WebApp viewer JavaScript") are not applicable.

**Key Differences from Other Editors:**
- Markdown/Spreadsheet/Screenplay: Load custom HTML/JS into WebView2, communicate via postMessage
- WebApp: Navigate directly to external URLs, no custom JS communication layer

**Test Results:**
- Build: Successful
- No code changes required

**Manual Testing Required:**
- [ ] Open .webapp file - navigates to URL
- [ ] Navigation (back/forward/refresh) works
- [ ] Download from web page works

---

## Phase 6: Cleanup and Documentation

**Completed:** Yes

**Files Removed:**
- `Core/Celbridge.UserInterface/Helpers/WebView2Messenger.cs` - Legacy message helper

**Files Created:**
- `Core/Celbridge.UserInterface/Plan/WebViewBridgeDeveloperGuide.md` - Developer guide for creating new editors

**Tasks Completed:**
1. ✅ Verified no external usages of `WebView2Messenger` or legacy message types (`JsMessage`, `JsPayloadMessage`)
2. ✅ Removed `WebView2Messenger.cs` 
3. ✅ Confirmed build succeeds after removal
4. ✅ Created developer guide with API reference and patterns
5. ✅ Updated `WebViewBridgeNotes.md` with phase completion

**Verification:**
- All three migrated editors (Markdown, Spreadsheet, Screenplay) use `WebViewBridge`
- WebApp viewer requires no bridge (URL browser without custom JS)
- Build succeeds with no references to removed code

---

## Final Summary

All phases of the WebView Bridge implementation are complete:

| Phase | Status | Description |
|-------|--------|-------------|
| 1 | ✅ | Core Infrastructure - C# bridge, JS module, tests |
| 2 | ✅ | Markdown Editor Migration |
| 3 | ✅ | Spreadsheet Editor Migration |
| 4 | ✅ | Screenplay Editor Migration |
| 5 | ✅ | WebApp Viewer (No migration needed) |
| 6 | ✅ | Cleanup and Documentation |

**Key Achievements:**
- Replaced ad-hoc message passing with standardized JSON-RPC 2.0 communication
- Eliminated race conditions through proper initialization handshake
- Removed scattered guard flags (`_isContentLoaded`, `isLoadingContent`, etc.)
- Unified document operations, dialog interactions, and theme handling
- Added debug logging for development troubleshooting
- Created comprehensive documentation for future editor development

**Files Summary:**

*Core Bridge Infrastructure:*
- `Core/Celbridge.UserInterface/Helpers/WebViewBridge.cs` - Main C# bridge class
- `Core/Celbridge.UserInterface/Helpers/WebViewBridgeTypes.cs` - Typed contracts
- `Core/Celbridge.UserInterface/Helpers/IWebViewMessageChannel.cs` - Testability interface
- `Core/Celbridge.UserInterface/Helpers/WebView2MessageChannel.cs` - Production implementation
- `Core/Celbridge.UserInterface/Helpers/WebViewLocalizationHelper.cs` - Localization extraction
- `Core/Celbridge.UserInterface/Web/webview-bridge.js` - JavaScript bridge module

*Tests:*
- `Celbridge.Tests/Helpers/WebViewBridgeTests.cs` - C# unit tests (NUnit)
- `Core/Celbridge.UserInterface/Web/webview-bridge.test.js` - JS unit tests (Vitest)

*Documentation:*
- `Core/Celbridge.UserInterface/Plan/WebViewBridge.md` - Design document
- `Core/Celbridge.UserInterface/Plan/WebViewBridgeNotes.md` - Implementation notes
- `Core/Celbridge.UserInterface/Plan/WebViewBridgeDeveloperGuide.md` - Developer guide
