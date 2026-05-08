---
name: webview
description: WebView devtools-style automation for HTML and contribution editors — click, fill, eval, query, screenshot, and observe network and console traffic.
---

# webview

The `webview` namespace drives WebView-backed editors: HTML viewers and contribution document editors. It exposes a devtools-style automation surface — click and fill simulated user input, evaluate JavaScript, query the DOM, take screenshots, and observe console and network traffic. Most webview tools require the document to be open and the right editor to have opened it.

## Must-knows

- **Most tools are gated by feature flags.** `webview_eval` requires `webview-dev-tools` and `webview-dev-tools-eval`; other tools require `webview-dev-tools`. Read `app_get_state` and check `featureFlags` before calling. If a flag is off, tell the user which flag is gating the action.
- **The right editor must have opened the document.** `document_get_state` returns an `editorId` for each open document. If you opened a `.html` expecting the HTML viewer but `editorId` is the code editor, webview tools won't work against it. See `webview_devtools`.
- **`webview_screenshot` requires the tab to be active.** WebView2 pauses rendering for inactive tabs, so an inactive tab cannot produce a frame. Activate the tab via `document_activate` first. See `webview_devtools`.
- **Synthetic events have `isTrusted: false`.** Handlers gated on `event.isTrusted` will not fire from `webview_click`. If a click appears to do nothing, use `webview_eval` to confirm the handler ran.
- **Console and network buffers survive reloads.** `webview_get_console` and `webview_get_network` return everything observed since the document opened, including across reloads.

## Tools

**User-input simulation.**

- `webview_click` — dispatch a synthetic click sequence.
- `webview_fill` — set the value of an input, textarea, select, or contenteditable, dispatching `input` and `change`.

**Evaluation and inspection.**

- `webview_eval` — evaluate JavaScript in the document's context. Returns the result as JSON.
- `webview_query` — `querySelector(All)` against the DOM.
- `webview_inspect` — structural snapshot of an element (attributes, computed styles, child shape).
- `webview_get_html` — serialised HTML of an element or the document.

**Observation.**

- `webview_get_console` — the document's console buffer.
- `webview_get_network` — the document's network log. `includeHeaders` and `includeBodies` widen the payload — opt in only when needed.
- `webview_screenshot` — capture the document as an image. Requires the tab to be active.

**Lifecycle.**

- `webview_reload` — reload the document.

## See also

- `webview_devtools` — feature flags, editor binding, activation, settle-time semantics.
- `webview_documents` — when to use a WebView-backed document and how the contribution-editor model works.
- `document_get_state` — discover which editor opened the document.
- `app_get_state` — feature-flag map.
