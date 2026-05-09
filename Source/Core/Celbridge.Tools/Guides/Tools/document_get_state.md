# document_get_state

Returns the visual state of the document editor: which document is active, how the editor is split into sections, and every open document with its section, tab order, active flag, and bound editor id. Use this to understand what the user is currently looking at before deciding which tools to invoke or which document to operate on.

The snapshot is taken via the command queue, so it observes state after any commands you've already enqueued.

## Returns

A JSON object with these fields:

- `activeDocument` (string) — resource key of the active document, or empty when no document is active.
- `sectionCount` (int) — number of visible editor sections (1-3).
- `openDocuments` (array) — every open document tab. Each entry has:
  - `resource` (string) — the document's resource key.
  - `sectionIndex` (int) — which section (0 = left, 1 = center, 2 = right) the tab lives in.
  - `tabOrder` (int) — position within that section's tab strip.
  - `isActive` (bool) — `true` for the active tab in its section.
  - `editorId` (string) — the bound editor id (e.g. `"celbridge.code-editor"`), or empty when no editor is bound yet.

## See also

- `app_get_state` — broader workspace state including focused panel and layout visibility.
- `document_open`, `document_activate`, `document_close` — operate on the documents this snapshot describes.
- `workspace_panels` — how editor sections relate to the surrounding panels.
