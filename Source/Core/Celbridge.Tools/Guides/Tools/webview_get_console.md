# webview_get_console

Reads the WebView's accumulated console buffer. Each entry is a `console.log`/`info`/`warn`/`error` call, an uncaught exception, or an unhandled promise rejection. The buffer survives reloads, so errors logged before a `webview_reload` remain visible afterwards.

See `webview_devtools` for the broader edit-reload-inspect loop.

## Parameters

- `resource` — resource key of an open document tab.
- `tail` — maximum number of recent entries to return after filtering. Default 100.
- `includeDebug` — when `true`, includes `console.debug` entries. Default `false` because debug output is typically high-volume.
- `sinceTimestampMs` — when greater than 0, returns only entries with `timestampMs` strictly greater than this value. Pass the largest `timestampMs` from a prior call to poll incrementally without redelivering older entries. `0` (default) disables this filter.

## Returns

JSON object with:

- `entries` — array of console records, each carrying `timestampMs`, `level`, `text`, and source metadata.
- `returned` — the count after filtering.
- `totalAccumulated` — the total number of entries the host has captured for this resource since it was opened. A growing gap between `returned` and `totalAccumulated` means the buffer is filling faster than you are polling.

## Polling pattern

```text
1. Call webview_get_console(resource) and read the largest entries[i].timestampMs.
2. Trigger an action (webview_click, webview_reload, ...).
3. Call webview_get_console(resource, sinceTimestampMs: <prior max>) to read only new entries.
```

## See also

- `webview_devtools` — cross-cutting concept guide.
- `webview_reload` — buffer survives across reloads.
- `webview_get_network` — same buffer-survives-reload model for fetch/XHR.
