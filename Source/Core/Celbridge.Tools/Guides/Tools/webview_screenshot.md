---
name: webview_screenshot
description: Captures a screenshot of an open WebView document, optionally clipped to a selector and optionally archived to the project tree.
---

# webview_screenshot

Captures a PNG or JPEG of an open WebView document. By default the image is returned inline as an MCP image content block alongside a JSON metadata text block. The document must be the active tab — WebView2 pauses rendering for inactive tabs, so an inactive tab fails fast rather than hanging.

See `webview_devtools` for the activation requirement, save-vs-return behaviour, format and `maxEdge` defaults, and when to pass `settleMs`. The detail below is parameter shape only — defer to that guide for the operational rules.

## Parameters

- `resource` — resource key of an open document tab. Must be the active document.
- `saveTo` — optional resource key or folder for archiving the image. Empty (default) skips the save. A trailing slash or extension-less value is treated as a folder; a path ending in `.png` / `.jpg` / `.jpeg` is treated as an exact destination. The extension must match `format`.
- `returnImage` — when `true` (default), returns the image inline so the multimodal model sees it. Setting `false` requires a non-empty `saveTo`; `false` with no `saveTo` is a hard error because the captured bytes would be discarded.
- `format` — `"jpeg"` (default) or `"png"`.
- `quality` — JPEG quality 1-100. Default 70. Ignored for PNG.
- `maxEdge` — longer-edge pixel cap. Default 768. `0` disables downscaling. Image token cost scales with pixel area, so doubling `maxEdge` quadruples cost.
- `selector` — optional CSS selector to clip the capture to a single element.
- `settleMs` — extra delay before capture, on top of the editor's content-ready signal. Default 0. Pass 500 (or higher, up to ~1000 for slow editors) after layout-changing operations such as `document_open`, route navigation, or any action that may still be composing the initial layout.

## Returns

When `returnImage` is `true`: an inline image content block plus a JSON text block. When `returnImage` is `false`: only the JSON metadata. The metadata payload is `{format, width, height, sizeBytes, resource, imageReturned}`. `resource` is the saved location if `saveTo` was used, otherwise `null`.

## Save destination resolution

Save resolution runs before capture, so a malformed `saveTo` fails before any work happens. The save itself routes through the binary write command, so capability gating, registry refresh, and path containment all apply.

## See also

- `webview_devtools` — cross-cutting concept guide; covers activation, save/return semantics, defaults, and `settleMs` rationale.
- `file_read_image` — read an already-saved screenshot back into context.
- `document_get_context` — confirm the tab is active before capture.
