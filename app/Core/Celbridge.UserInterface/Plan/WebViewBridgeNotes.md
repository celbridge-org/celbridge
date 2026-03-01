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
- `Core/Celbridge.UserInterface/WebAssets/webview-bridge.js` - JavaScript bridge module with JSDoc
- `Core/Celbridge.UserInterface/WebAssets/webview-bridge.test.js` - 18 Vitest unit tests
- `Core/Celbridge.UserInterface/WebAssets/package.json` - npm config for Vitest
- `Core/Celbridge.UserInterface/WebAssets/vitest.config.js` - Vitest configuration
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
- `Core/Celbridge.UserInterface/WebAssets/webview-bridge.js` - Added document.onRequestSave() event
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

*(To be filled during implementation)*

---

## Phase 4: Screenplay Editor Migration

*(To be filled during implementation)*

---

## Phase 5: WebApp Viewer Migration

*(To be filled during implementation)*

---

## Phase 6: Cleanup and Documentation

*(To be filled during implementation)*
