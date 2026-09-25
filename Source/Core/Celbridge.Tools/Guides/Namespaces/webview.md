# webview

The `webview` namespace drives WebView-backed editors: HTML viewers and contribution document editors. It exposes a devtools-style automation surface — click and fill simulated user input, evaluate JavaScript, query the DOM, take screenshots, and observe console and network traffic. Most webview tools require the document to be open and the right editor to have opened it.

## Must-knows

- **Most tools are gated by feature flags.** `webview_eval` requires `webview-dev-tools` and `webview-dev-tools-eval`, and the rest require `webview-dev-tools`. In the Web group of the Features section of Project Settings, the user sees `webview-dev-tools` as Developer Tools and `webview-dev-tools-eval` as Run JavaScript in Editors. Both are on by default, and a project can switch either off. Check `featureFlags` from `app_get_state` before calling.
- **The right editor must have opened the document.** `document_get_state` returns an `editorId` per open document. A `.html` page can be inspected in the HTML viewer and the HTML editor, but not in the general code editor, which shows only the source. See `webview_devtools`.
- **A call acts on one frame.** With no `frame`, it acts on the page's content frame, such as the page the HTML editor previews, or else on the page itself. `frame: "top"` names the page itself. Every result names the frame it acted on. See `webview_devtools`.
- **`webview_screenshot` requires the tab to be active.** WebView2 pauses rendering for inactive tabs. Activate via `document_activate` first.
- **Synthetic events have `isTrusted: false`.** Handlers gated on `event.isTrusted` will not fire from `webview_click`. If a click appears to do nothing, use `webview_eval` to confirm.
- **Canvas-painted UI is invisible to selectors.** Pages that draw controls to a `<canvas>` have no DOM elements to query or click. Drive interaction by dispatching a synthetic `MouseEvent` to the canvas via `webview_eval` at the page-expected coordinates.
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
