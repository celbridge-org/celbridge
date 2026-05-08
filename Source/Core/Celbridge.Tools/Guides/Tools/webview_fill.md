---
name: webview_fill
description: Sets the value of an input, textarea, select, or contenteditable matched by a CSS selector and fires bubbling input/change events.
---

# webview_fill

Sets the value of a form control (or `contenteditable` element) inside an open WebView document and dispatches the bubbling `input` and `change` events that frameworks listen for. The native `HTMLInputElement` / `HTMLTextAreaElement` / `HTMLSelectElement` setter is used, so React's synthetic event system, Lit, Vue, and Svelte all observe the change.

See `webview_devtools` for the edit-reload-inspect loop and the readiness contract.

## Parameters

- `resource` — resource key of an open document tab.
- `selector` — non-empty CSS selector. Only the first match receives the value.
- `value` — string to assign. For `contenteditable` elements the value is set as `textContent`.

## Returns

JSON object with `selector`, `tag`, and the value read back after assignment. Compare the read-back value against `value` to confirm the assignment took effect against any framework-managed binding.

## Supported targets

Only `<input>`, `<textarea>`, `<select>`, and `contenteditable` elements are accepted. Any other selector causes the call to fail fast rather than silently no-op.

## See also

- `webview_devtools` — cross-cutting concept guide.
- `webview_query` — locate the input first when the selector is uncertain.
- `webview_click` — for non-text controls (buttons, checkboxes, radios).
