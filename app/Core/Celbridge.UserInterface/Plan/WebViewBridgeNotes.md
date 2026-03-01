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

*(To be filled during implementation)*

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
