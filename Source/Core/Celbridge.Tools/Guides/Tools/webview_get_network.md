# webview_get_network

Reads the WebView's accumulated network buffer of `fetch` and `XMLHttpRequest` activity. The default payload is a header- and body-free summary so casual polling stays cheap. The buffer survives reloads, mirroring `webview_get_console`.

## Parameters

- `resource` — resource key of an open document tab.
- `tail` — maximum number of recent entries to return after filtering. Default 100.
- `includeHeaders` — when `true`, populates `requestHeaders` and `responseHeaders` on each entry. Default `false`.
- `includeBodies` — when `true`, populates `requestBodyDescription` and `responseBody`. Response bodies are captured up to ~16KB and arrive truncated past that. Binary responses appear as a placeholder. Default `false`.
- `sinceTimestampMs` — when greater than 0, returns only entries with `startTimeMs` strictly greater than this value. Pass the largest `startTimeMs` from a prior call to poll incrementally. `0` (default) disables this filter.

## Returns

JSON object with:

- `entries` — array of `{id, type, method, url, status, startTimeMs, durationMs, requestSize, responseSize}`. When the corresponding flag is set: `requestHeaders`, `responseHeaders`, `requestBodyDescription`, `responseBody`. Failed requests carry an `error` field.
- `returned` — count after filtering.
- `totalAccumulated` — total entries the host has captured for this resource since it was opened.

## Payload control

Bodies dominate context cost — opt in only when you need to read them. Headers are cheaper but still meaningful for chatty endpoints. A common pattern is to call without flags first, find the entry of interest by URL or status, then re-call with `includeHeaders` or `includeBodies` for the same time window.
