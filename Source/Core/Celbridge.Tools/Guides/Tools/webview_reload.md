# webview_reload

Reloads the WebView associated with an open document so package code reinitialises against the latest files on disk. Call this after editing HTML, CSS, or JavaScript in a contribution package to see the changes.

## Parameters

- `resource` — resource key of an open document tab.
- `clearCache` — when `true` (default), evicts the WebView HTTP cache before reload so newly-edited JS, CSS, and image sub-resources are refetched. Pass `false` when no sub-resources have changed. The cache eviction is profile-wide: it affects every document sharing the same WebView profile. It applies only when the page itself reloads.

## Reloading a frame

When the call acts on a frame, only that frame's page reloads and the cache is left alone. Project files are never cached, so the page and its project assets always load fresh. In the HTML editor this reloads the previewed page without touching the editor. The preview does not reload by itself when the file changes, so call this after writing the page or a file it uses, and before inspecting the result. It reloads the page the frame is showing: if the page has navigated itself elsewhere, that page reloads rather than the document. Pass `frame: "top"` to reload the editor itself.

## Returns

`"ok"` on success, followed by a JSON block naming the `frame` that was reloaded.

## What gets discarded

The reload is destructive by design. In-page state, transient editor selection, and Monaco's undo history (if any) are all wiped. Console and network buffers persist on the host so prior errors and requests remain readable through `webview_get_console` and `webview_get_network`.
