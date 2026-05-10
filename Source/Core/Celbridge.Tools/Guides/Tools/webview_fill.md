# webview_fill

Sets the value of a form control (or `contenteditable` element) inside an open WebView document and dispatches the bubbling `input` and `change` events that frameworks listen for. The native `HTMLInputElement` / `HTMLTextAreaElement` / `HTMLSelectElement` setter is used, so React's synthetic event system, Lit, Vue, and Svelte all observe the change.

## Parameters

- `resource` — resource key of an open document tab.
- `selector` — non-empty CSS selector. Only the first match receives the value.
- `value` — string to assign. For `contenteditable` elements the value is set as `textContent`.

## Returns

JSON object with `selector`, `tag`, and the value read back after assignment. Compare the read-back value against `value` to confirm the assignment took effect against any framework-managed binding.

## Supported targets

Only `<input>`, `<textarea>`, `<select>`, and `contenteditable` elements are accepted. Any other selector causes the call to fail fast rather than silently no-op.
