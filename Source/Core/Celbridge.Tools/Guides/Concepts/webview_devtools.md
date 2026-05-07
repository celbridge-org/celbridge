---
name: webview_devtools
description: The webview_* tool surface for inspecting and driving a running contribution editor or HTML viewer; the edit-reload-inspect loop, screenshots, and gotchas.
---

# WebView devtools

The `webview_*` tools give the agent a feedback loop into a running contribution editor or HTML viewer WebView, so the agent can iterate on package code without needing the user to reload and paste back errors.

## Edit-reload-inspect loop

1. Edit the package's HTML/CSS/JS files with `document_*` or `file_*` tools.
2. Call `webview_reload(resource)` to reload the WebView for the open document. The package code reinitialises from scratch and Monaco's undo history (if any) is wiped — the reload is destructive by design. By default the WebView HTTP cache is cleared first so newly-edited JS/CSS/image sub-resources are refetched. Pass `clearCache: false` to skip the cache clear when no sub-resources have changed.
3. Call `webview_get_console(resource)` immediately after a reload to read parse errors, runtime exceptions, or warnings the editor logged. The console buffer is preserved across reloads, so errors logged before the reload remain readable after.
4. Inspect the rendered DOM with `webview_get_html(resource, selector?)`, `webview_query(resource, ...)`, and `webview_inspect(resource, selector)` to confirm markup, find elements, and read computed styles.
5. Exercise the editor with `webview_click(resource, selector)`, `webview_fill(resource, selector, value)`, or `webview_eval(resource, expression)`.
6. Capture a visual snapshot with `webview_screenshot(resource, ...)` when the rendered output matters. Observe network activity with `webview_get_network(resource)`.

## Confirm the right editor opened the document

`document_get_context` returns an `editorId` for each open document (e.g. `celbridge.html-viewer`, `celbridge.code-editor`). If you opened a `.html` file expecting the HTML viewer but `editorId` is the code editor, the WebView devtools won't work against it — check the resource's file association before calling any `webview_*` tool.

## Programmatic clicks have `isTrusted: false`

`webview_click` dispatches a real `MouseEvent` sequence (`mousedown`, `mouseup`, `click`) that bubbles, but because the events are synthetic, handlers that gate on `event.isTrusted` will not fire. If a click appears to do nothing, use `webview_eval` to confirm the expected effect ran (read a counter the handler increments, or check a class the handler toggles). The DevTools-only `getEventListeners()` helper is not available to `webview_eval` — calling it raises a `ReferenceError`.

## `webview_fill` works with most framework input bindings

It assigns the value through the native `HTMLInputElement` / `HTMLTextAreaElement` / `HTMLSelectElement` setter, then dispatches bubbling `input` and `change` events, so React's synthetic event system, Lit, Vue, and Svelte all observe the change. Only `<input>`, `<textarea>`, `<select>`, and `contenteditable` elements are accepted; any other selector causes the call to fail fast.

## `webview_get_network` defaults to a header- and body-free summary

Each entry includes URL, method, status, timing, and sizes. Set `includeHeaders` or `includeBodies` to widen the payload — bodies dominate context, so opt in only when needed. Response bodies are captured up to ~16KB with truncation markers; binary responses appear as a placeholder. The buffer survives reloads, like the console buffer.

## `webview_screenshot` requires the document to be the active tab

WebView2 pauses rendering for inactive tabs, so an inactive tab cannot produce a frame and the tool fails fast rather than hanging. Before calling, ensure the document is open (`document_open`) and that it is the active document (check `document_get_context` -> `activeDocument`). If the user switches tabs during the capture, the tool times out within ~5 seconds with an explanatory error — re-activate the tab and retry.

## `webview_screenshot` returns the image inline by default

The captured image arrives as an MCP image content block that the multimodal model sees directly, alongside a JSON metadata text block. No project file is created unless `saveTo` is specified. Use `saveTo` to additionally archive the image into the project tree:

- `saveTo: ""` (default) — ephemeral capture, nothing written to disk.
- `saveTo: "screenshots/"` — write into that folder with an auto-generated filename (trailing slash or no extension is treated as a folder reference).
- `saveTo: "docs/output.png"` — write to that exact resource key. The file extension must match the format (`.jpg`/`.jpeg` for `jpeg`, `.png` for `png`).

For save-only flows where the image bytes aren't needed in the model's context (e.g. publishing a snapshot for the user to view), pass `returnImage: false` along with `saveTo` to skip the inline-token cost. `returnImage: false` with no `saveTo` is a hard error.

## After a layout-changing operation, pass `settleMs`

The screenshot tool waits for the editor's content-ready signal before capturing, but that signal is package-defined and can fire before panel animations finish, fonts load, or async resources arrive. After `document_open`, after a click that triggers route navigation, or any time the package may still be composing its initial layout, pass `settleMs: 500` (or higher — 1000 is fine for slow editors). A small fixed paint backstop is always applied, so omitting `settleMs` still gives the page one paint cycle of headroom for a static-on-arrival target.

Defaults are JPEG quality 70 with the longer edge capped at 768 pixels — roughly 590 image tokens per capture for a 4:3 viewport. Pass `maxEdge: 1024` (or higher) when you need to read fine on-screen text such as code in an editor. Pass `maxEdge: 0` to disable downscaling entirely. Image token cost scales with pixel area: doubling `maxEdge` quadruples the token cost. Pass `format: "png"` for lossless output. Pass `selector` to clip to a specific element. The metadata payload is `{format, width, height, sizeBytes, resource, imageReturned}`.

## Reading saved images: `file_read_image`

To inspect an image already in the project tree (a previously-saved screenshot, a user-supplied PNG), call `file_read_image(resource)`. It returns the pixels as an MCP image content block, the same way `webview_screenshot` does. Supported: JPEG, PNG, GIF, WebP. For non-image binaries, use `file_read_binary` — `file_read_image` rejects other types because the inline image transport only makes sense for visual content.

## Readiness contract

Every inspection and eval tool waits up to 5 seconds for the editor's content-ready signal before dispatching. For contribution editors that means `celbridge.notifyContentLoaded()`. For the HTML viewer it means the WebView's NavigationCompleted. If a tool times out with a `content-ready` message, the editor never signalled readiness — check the console for an unhandled exception during init.

## `webview_query` modes

Pass exactly one of `role` (with optional `name`), `text`, or `selector`. Role queries combine the explicit `role` attribute and the implicit role for the element's tag (button -> button, h2 -> heading, nav -> navigation). Returned `selector` strings are stable enough to pass straight to `webview_inspect`.

## Supported targets

Works on any open document editor — text, markdown, HTML viewers, and custom contribution editors. Excluded: external-URL `.webview` documents and editors whose package opts out of devtools. If you're unsure whether a given document qualifies, just call the tool — the error message will tell you. The resource key must match an open document tab.

## `webview_eval` is gated by an extra feature flag

It is an arbitrary code execution primitive, so it requires both `webview-dev-tools` and `webview-dev-tools-eval`. If unavailable, the other `webview_*` tools may still work. Tell the user the flag is off and why it is gated separately.

## Available from Python and MCP, not from package JS

The Python proxy runs on behalf of the user (the trust root), so `cel.webview.eval(...)` works there. The JavaScript proxy runs inside third-party package code, so the host denies any `webview.*` call arriving from a contribution editor — regardless of what the package's `requires_tools` declares. This blocks a cross-document attack vector where one editor's JavaScript could eval against another open document. Do not declare `webview.*` in a package's `requires_tools`.
