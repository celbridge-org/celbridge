---
name: document
description: Document editor tools — open, close, activate tabs, and snapshot the editor state. Read this before reasoning about "the open file" or directing the user's attention.
---

# document

The `document` namespace controls the documents panel: which files are open as tabs, which is active, and how the editor sections are split. It pairs with the `file` namespace, which operates on file contents directly. Use `document` when the user is talking about an editor tab; use `file` when they're talking about the file on disk.

## Must-knows

- **Edits write straight to disk; the editor reloads from disk and Monaco's undo history is wiped.** This is the file save model — see `file_changes` for the full rules. The document namespace itself does not edit content; it activates and closes tabs. To modify content, use the `file` namespace.
- **Resolve "the file" against the active document first.** When the user refers to "this file" or asks about "line 12" without naming a file, call `document_get_state` and check `activeDocument` before falling back to a project-wide search. See `workspace_panels`.
- **Closing always saves.** There is no "discard unsaved changes" prompt. The pending auto-save is flushed on close. See `file_changes`.
- **`document_open` activates by default only when `activate: true` is passed.** Otherwise it opens the tab without changing the active document, leaving the user on the tab they're currently looking at.

## Tools

- `document_open` — open a file as an editor tab. Optional `sectionIndex`, `forceReload`, `activate`.
- `document_activate` — bring an already-open tab to the foreground.
- `document_close` — close one or more tabs (accepts a single resource key or a JSON array of keys). `forceClose` skips the editor's can-close check.
- `document_get_state` — snapshot of editor state: active document, section count, every open tab with its position and bound editor id.

## See also

- `file_changes` — the file save model and how programmatic edits interact with open documents.
- `editing_documents` — overall lifecycle of opening, editing, and closing documents.
- `workspace_panels` — how the documents panel relates to surrounding panels and how to resolve ambiguous file references.
- `webview_documents`, `webview_devtools` — when the active document is a WebView-backed editor.
