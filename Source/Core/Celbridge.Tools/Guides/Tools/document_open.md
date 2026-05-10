# document_open

Opens a document in the editor. By default the document opens in the background — the user's current active tab is preserved. Pass `activate: true` (or follow up with `document_activate`) to bring it to the foreground.

You do not need to open a document to edit its file. The `file_*` tools work on any file under the content root. Open the document when the user should see the result, or when you intend to drive a webview-bound editor afterwards.

## Parameters

### sectionIndex

- `0` — left section.
- `1` — center section.
- `2` — right section.
- `-1` (default) — open in the currently active section.

Any other value is rejected.

### forceReload

When `true`, reload the document from disk even if it is already open. The normal save model already reloads on external writes, so this is rarely needed.

### activate

When `true`, the opened document becomes the active tab in its section. Default `false`.

## Returns

A status string:

- `"opened"` — the document is now open. Also returned when the document was already open and `activate: true` simply moved focus to it.
- `"cancelled"` — the open was a no-op because an existing tab refused to close (e.g. a confirmation prompt was declined). No error; surface to the user as a soft outcome.

An error message is returned if the operation failed.
