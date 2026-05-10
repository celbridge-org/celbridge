# document

The `document` namespace controls the documents panel: which files are open as tabs, which is active, and how the editor sections are split. It pairs with the `file` namespace, which operates on file contents directly. Use `document` when the user is talking about an editor tab; use `file` when they are talking about the file on disk.

## Must-knows

- **Edits write straight to disk; the editor reloads from disk and Monaco's undo history is wiped.** The document namespace itself does not edit content; it activates and closes tabs. To modify content, use the `file` namespace. See `file_changes` for the full save model.
- **Resolve "the file" against the active document first.** When the user refers to "this file" without naming it, call `document_get_state` and check `activeDocument` before falling back to a project-wide search. See `workspace_panels`.
- **Closing always saves.** There is no "discard unsaved changes" prompt. The pending auto-save is flushed on close.
- **`document_open` does not activate by default.** Pass `activate: true` to make the opened tab the foreground tab; otherwise the user's current tab is preserved.

## Tools

- `document_open` — open a file as an editor tab. Optional `sectionIndex`, `forceReload`, `activate`.
- `document_activate` — bring an already-open tab to the foreground.
- `document_close` — close one or more tabs (single resource key or JSON array of keys). `forceClose` skips the editor's can-close check.
- `document_get_state` — snapshot of editor state: active document, section count, every open tab with its position and bound editor id.
