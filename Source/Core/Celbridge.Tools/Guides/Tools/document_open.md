---
name: document_open
description: Open a document in the editor, optionally targeting a specific section and activating it.
---

# document_open

Opens a document in the editor. By default the document is opened in the background — the user's current active tab is preserved. Pass `activate: true` (or follow up with `document_activate`) when you want to bring it to the foreground.

You don't need to open a document to edit its file. The `file_*` tools work on any file under the content root. Open the document when the user should see the result, or when you intend to drive a webview-bound editor afterwards. See `editing_documents` for guidance on when to open vs. edit-without-opening.

## Parameters

### fileResource

Resource key of the file to open. See `resource_keys`.

### sectionIndex

Which editor section to open the document in.

- `0` — left section.
- `1` — center section.
- `2` — right section.
- `-1` (default) — open in the currently active section.

Any other value is rejected.

### forceReload

When `true`, reload the document from disk even if it is already open. Useful if you have written to the file out-of-band and want the editor's buffer to refresh immediately. The normal save model already reloads on external writes, so this is rarely needed.

### activate

When `true`, the opened document becomes the active tab in its section. Default is `false` — the document opens in the background and the user's current tab is preserved.

## Returns

A status string:

- `"opened"` — the document is now open. This is also the result when the document was already open and `activate: true` simply moved focus to it.
- `"cancelled"` — the open was a no-op because an existing tab refused to close (e.g. a confirmation prompt was declined). No error is raised; treat this as a soft outcome to surface to the user.

An error message is returned if the operation failed.

## See also

- `document_activate` — activate an already-open tab without re-opening.
- `document_close` — close one or more documents.
- `document_get_state` — discover which documents are currently open and which is active.
- `editing_documents`, `file_changes` — when to open and how the save model behaves.
