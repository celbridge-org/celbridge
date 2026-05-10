# webview_click

Drives a click against an open contribution editor or HTML viewer by selector. The first match receives a `mousedown`, `mouseup`, and `click` sequence with bubbling enabled, so React, Lit, Vue, and Svelte handlers all observe the click.

## Parameters

- `resource` — resource key of an open document tab. The document must be opened by an editor that supports webview devtools.
- `selector` — non-empty CSS selector. Only the first match is clicked.

## Returns

JSON object with `selector`, `tag`, `visible`, `rect`, and `isTrusted` (always `false`).

## Gotchas

- Synthetic events: handlers gated on `event.isTrusted` will not fire. If a click appears to do nothing, follow up with `webview_eval` to read a side effect, or with `webview_inspect` to verify a class change.
- The selector must match a currently mounted element. If the matching element is conditionally rendered, query for it first with `webview_query` to confirm presence.
- Canvas-painted UI cannot be targeted by selector. See the `webview` namespace guide for the workaround.
