# webview_reload

Reloads the WebView associated with an open document so package code reinitialises against the latest files on disk. Call this after editing HTML, CSS, or JavaScript in a contribution package to see the changes.

## Parameters

- `resource` — resource key of an open document tab.
- `clearCache` — when `true` (default), evicts the WebView HTTP cache before reload so newly-edited JS, CSS, and image sub-resources are refetched. Pass `false` when no sub-resources have changed. The cache eviction is profile-wide: it affects every document sharing the same WebView profile.

## Returns

`"ok"` on success.

## What gets discarded

The reload is destructive by design. In-page state, transient editor selection, and Monaco's undo history (if any) are all wiped. Console and network buffers persist on the host so prior errors and requests remain readable through `webview_get_console` and `webview_get_network`.
