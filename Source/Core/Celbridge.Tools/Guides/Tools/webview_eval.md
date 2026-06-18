# webview_eval

Runs an arbitrary JavaScript expression in the WebView's main world and returns the JSON-encoded value. Useful for reading state that no other `webview_*` tool exposes, confirming side effects after `webview_click`, or invoking a function the editor exports on `window`.

## Feature flag gating

`webview_eval` is gated by **two** feature flags, not one:

- `webview-dev-tools` — enables the wider `webview_*` family.
- `webview-dev-tools-eval` — gates this tool specifically because it is an arbitrary code execution primitive.

If either flag is off, the call fails with an explanatory error. Tell the user which flag is gating the action and that `webview_eval` is gated separately on purpose. Other `webview_*` tools may continue to work when only `webview-dev-tools-eval` is off. Check both flags via `app_get_state` before recommending workarounds.

## Parameters

- `resource` — resource key of an open document tab.
- `expression` — a single JavaScript expression. Multi-statement code returns `null`. Wrap multi-statement logic in an IIFE that returns explicitly: `(() => { const x = 1; return x + 1; })()`.

## Returns

The JSON-serialised result of the expression. `null` is returned when the expression evaluates to `undefined` or `null`.

## Gotchas

- The DevTools-only `getEventListeners()` helper does not exist in this context. Calling it raises a `ReferenceError`.
- The expression body may contain sensitive output (cookies, storage values). The host logs only the resource and the expression length at info level. Treat the contents as you would any other arbitrary code execution path.
- Only available from Python and from the MCP transport. The JavaScript proxy refuses `webview.*` calls from package code regardless of `[permissions] tools`. Do not declare `webview.*` in a package manifest.
