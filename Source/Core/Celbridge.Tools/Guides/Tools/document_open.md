# document_open

Opens a document in the editor. By default the document opens in the background — the user's current active tab is preserved. Pass `activate: true` (or follow up with `document_activate`) to bring it to the foreground.

You do not need to open a document to edit its file. The `file_*` tools work on any file under the content root. Open the document when the user should see the result, or when you intend to drive a webview-bound editor afterwards.

## Parameters

### section

The tab strip to open the document in, as one of `MainLeft`, `MainRight`, `BottomLeft`, `BottomRight`, `SideTop`, `SideBottom`. Matching is case-insensitive; any other value is rejected. See `document_get_state` for what each section is and when it exists.

Empty (the default) opens in the currently active section.

Naming a secondary section (`MainRight`, `BottomRight`, `SideBottom`) while its area is not split splits the area so the document opens where it was asked for. If that leaves the area's primary section empty — the area held nothing beforehand — the split folds straight back and the document ends up in the primary section, because a split section is never left empty. Naming a section in a collapsed area opens there without expanding the area, so the user does not see the document until they show it again — prefer `MainLeft` or the default when you want the document on screen.

### forceReload

When `true`, reload the document from disk even if it is already open. The normal save model already reloads on external writes, so this is rarely needed.

### activate

When `true`, the opened document becomes the active tab in its section. Default `false`.

## Returns

A status string:

- `"opened"` — the document is now open. Also returned when the document was already open and `activate: true` simply moved focus to it.
- `"cancelled"` — the open was a no-op because an existing tab refused to close (e.g. a confirmation prompt was declined). No error; surface to the user as a soft outcome.

An error message is returned if the operation failed.
