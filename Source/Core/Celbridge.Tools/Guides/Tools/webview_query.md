---
name: webview_query
description: Locates elements in a WebView document by ARIA role plus name, by visible text, or by CSS selector — exactly one mode per call.
---

# webview_query

Finds elements inside an open WebView document. The returned `selector` strings are stable enough to pass straight to `webview_inspect`, `webview_click`, or `webview_fill`. Pick the mode that best matches what you know about the target.

See `webview_devtools` for the broader edit-reload-inspect loop.

## Modes — exactly one per call

Pass exactly one of `role`, `text`, or `selector`. Passing zero or more than one is an error.

### `role` (with optional `name`)

ARIA role lookup that combines the explicit `role` attribute with the implicit role for the element's tag (`button` → `button`, `h2` → `heading`, `nav` → `navigation`). When `name` is also supplied, results are filtered by accessible-name substring (case-insensitive). `name` is ignored when `role` is empty.

### `text`

Visible-text substring (case-insensitive). Matches against rendered text content, so hidden elements do not appear.

### `selector`

A CSS selector. Returns up to `maxResults` matching elements.

## Parameters

- `resource` — resource key of an open document tab.
- `role` — ARIA role string. Empty when not querying by role.
- `name` — accessible-name substring filter. Ignored unless `role` is also set.
- `text` — visible-text substring. Empty when not querying by text.
- `selector` — CSS selector. Empty when not querying by selector.
- `maxResults` — maximum matches to return. Default 20.

## Returns

JSON object with:

- `mode` — which of `role`, `text`, or `selector` was used.
- `totalMatches` — total matches found before the `maxResults` cap.
- `returned` — number of entries in `elements` after the cap.
- `elements` — each entry carries a stable `selector` plus tag, visible flag, rect, role, and accessible name.

## Zero-match results

When `totalMatches` is `0` the response carries a guide pointer alongside the value. Common causes: the selector or role does not match the rendered DOM, the element is conditionally rendered and not mounted yet, the document has not finished its content-ready handshake, or the page paints its UI to a `<canvas>` (where there is no DOM to query — see the `webview` namespace guide).

## See also

- `webview_devtools` — cross-cutting concept guide.
- `webview_inspect` — richer view of a single element by selector.
- `webview_click`, `webview_fill` — drive interactions against the returned selectors.
