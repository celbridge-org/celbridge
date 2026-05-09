# webview_get_html

Returns serialised HTML for an open document, optionally scoped to a subtree by CSS selector and pruned to a maximum depth. Use this to confirm rendered markup after a click, fill, or reload, or to read the layout the editor produced from package code.

See `webview_devtools` for the edit-reload-inspect loop.

## Parameters

- `resource` — resource key of an open document tab.
- `selector` — optional CSS selector. Empty (default) returns the full document. When set, the first match's `outerHTML` is returned.
- `maxDepth` — maximum tree depth. Children beyond it are replaced with a placeholder node so payloads stay bounded. Default 8. The host clamps the value to the range `[0, 50]`.

## Returns

JSON object with `selector` (the resolved selector, or document root) and `html` (the serialised markup with depth pruning applied).

## Redactions

`<script>` and `<style>` element bodies are redacted to control payload size. The opening and closing tags are preserved, so element structure remains readable. If you need the script source itself, read the package file directly with `file_read_text` rather than trying to extract it from the page.

## See also

- `webview_devtools` — cross-cutting concept guide.
- `webview_query` — find a stable selector when you don't already have one.
- `webview_inspect` — richer single-element view (attributes, computed styles, role, rect).
