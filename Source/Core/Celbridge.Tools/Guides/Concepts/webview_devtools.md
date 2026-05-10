# WebView devtools

The `webview_*` tools give the agent a feedback loop into a running contribution editor or HTML viewer WebView, so the agent can iterate on package code without needing the user to reload and paste back errors.

## Edit-reload-inspect loop

1. Edit the package's HTML/CSS/JS files with `document_*` or `file_*` tools.
2. `webview_reload(resource)` — reinitialises package code from disk. Destructive: in-page state and Monaco's undo history are wiped. The HTTP cache is cleared by default; pass `clearCache: false` when no sub-resources have changed.
3. `webview_get_console(resource)` — read parse errors and exceptions. The console buffer survives reloads.
4. Inspect the rendered DOM with `webview_get_html`, `webview_query`, `webview_inspect`.
5. Drive interaction with `webview_click`, `webview_fill`, `webview_eval`.
6. Capture a visual with `webview_screenshot`. Observe traffic with `webview_get_network`.

## Confirm the right editor opened the document

`document_get_state` returns an `editorId` per open document (e.g. `celbridge.html-viewer`, `celbridge.code-editor`). If you opened a `.html` file expecting the HTML viewer but `editorId` is the code editor, devtools will not work against it — check before any `webview_*` call.

## Synthetic events have `isTrusted: false`

`webview_click` dispatches a real `MouseEvent` sequence (`mousedown`, `mouseup`, `click`) that bubbles, but handlers gated on `event.isTrusted` will not fire. If a click appears to do nothing, use `webview_eval` to confirm the expected effect ran. The DevTools-only `getEventListeners()` helper is unavailable to `webview_eval` — calling it raises `ReferenceError`.

## `webview_fill` works with most framework input bindings

It assigns the value through the native `HTMLInputElement` / `HTMLTextAreaElement` / `HTMLSelectElement` setter, then dispatches bubbling `input` and `change` events, so React, Lit, Vue, and Svelte all observe the change. Only `<input>`, `<textarea>`, `<select>`, and `contenteditable` are accepted; any other selector fails fast.

## `webview_get_network` defaults to a header- and body-free summary

Each entry includes URL, method, status, timing, and sizes. Set `includeHeaders` or `includeBodies` to widen the payload — bodies dominate context, so opt in only when needed. Response bodies are captured up to ~16KB with truncation markers; binary responses appear as a placeholder. Buffer survives reloads.

## `webview_screenshot` requires the document to be the active tab

WebView2 pauses rendering for inactive tabs, so an inactive tab cannot produce a frame and the tool fails fast. Activate the tab via `document_activate` first. If the user switches tabs during the capture, the tool times out within ~5 seconds.

The captured image arrives inline as an MCP image content block alongside JSON metadata. Without `saveTo` the capture is ephemeral. With `saveTo: "screenshots/"` the host writes into that folder with an auto-generated filename; with `saveTo: "docs/output.png"` the host writes to that exact key. The extension must match `format` (`.jpg`/`.jpeg` for `jpeg`, `.png` for `png`). Pass `returnImage: false` together with `saveTo` to skip the inline-token cost; `returnImage: false` with no `saveTo` is a hard error.

After a layout-changing operation (`document_open`, route navigation, async resource arrival) pass `settleMs: 500` (or up to ~1000 for slow editors) on top of the editor's content-ready signal. A small fixed paint backstop is always applied.

Defaults are JPEG quality 70 with the longer edge capped at 768 pixels — roughly 590 image tokens per capture for a 4:3 viewport. Image token cost scales with pixel area. Pass `maxEdge: 1024` (or higher) when fine on-screen text matters; `maxEdge: 0` disables downscaling entirely.

## Reading saved images

To inspect an image already in the project tree, use `file_read_image` (JPEG, PNG, GIF, WebP). For other binaries use `file_read_binary`.

## Readiness contract

Every inspection and eval tool waits up to 5 seconds for the editor's content-ready signal before dispatching. For contribution editors that means `celbridge.notifyContentLoaded()`; for the HTML viewer it means the WebView's `NavigationCompleted`. A `content-ready` timeout means the editor never signalled — check the console for an unhandled exception during init.

## `webview_query` modes

Pass exactly one of `role` (with optional `name`), `text`, or `selector`. Role queries combine the explicit `role` attribute and the implicit role for the element's tag (`button` -> `button`, `h2` -> `heading`, `nav` -> `navigation`). Returned `selector` strings are stable enough to pass straight to `webview_inspect`.

## Supported targets

Any open document editor — text, markdown, HTML viewers, custom contribution editors. Excluded: external-URL `.webview` documents, and editors whose package opts out. The resource key must match an open tab.

## `webview_eval` is gated by an extra feature flag

Both `webview-dev-tools` and `webview-dev-tools-eval` must be on, because `webview_eval` is an arbitrary code execution primitive. If the eval flag is off, the rest of the `webview_*` family may still work.

## Available from Python and MCP, not from package JS

The Python proxy runs on behalf of the user (the trust root). The JavaScript proxy runs inside third-party package code, so the host denies any `webview.*` call arriving from a contribution editor regardless of `requires_tools`. This blocks a cross-document attack vector. Do not declare `webview.*` in a package's `requires_tools`.
