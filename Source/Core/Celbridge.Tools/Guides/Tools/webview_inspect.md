# webview_inspect

Describes a single element in the WebView in detail. Use this after `webview_query` returns a stable selector, or when you already know the selector and want the structured view rather than raw `outerHTML`.

See `webview_devtools` for the broader edit-reload-inspect loop.

## Parameters

- `resource` — resource key of an open document tab.
- `selector` — non-empty CSS selector. The first match is described.
- `childPreviewLimit` — number of children to include in the preview. The full child count is always reported separately. Default 5.

## Returns

JSON object with:

- `tag` — lowercase tag name.
- `selector` — a stable selector for the element (often more specific than the input).
- `role` — ARIA role, combining explicit and implicit roles.
- `accessibleName` — the computed accessible name.
- `attributes` — a map of attribute name to value.
- `visible` — whether the element has layout and is not `display: none` or `visibility: hidden`.
- `rect` — bounding-rect in CSS pixels.
- `computedStyles` — the resolved styles the host considers relevant.
- `children` — preview slice plus the full child count.

## See also

- `webview_devtools` — cross-cutting concept guide.
- `webview_query` — find the selector first.
- `webview_get_html` — when you want raw markup for a subtree instead of structured fields.
