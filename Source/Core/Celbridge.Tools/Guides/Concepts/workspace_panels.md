# Workspace panels

A loaded project shows several panels around a central editor area:

- **Explorer** — the project file tree. `explorer_*` tools create, move, rename, and delete resources. `explorer_undo` / `explorer_redo` reverse file system operations only — they cannot undo document text edits.
- **Documents** — the central editor area. Files open as tabs across up to three sections (sectionIndex 0, 1, 2 from left to right). `document_*` tools open, close, activate, and inspect tabs; `file_*` tools edit content.
- **Inspector** — contextual properties for the selected resource.
- **Search** — full-text search UI. From the agent, use `file_grep` for the same purpose.
- **Console** — a Python REPL for interactive scripting.

## Resolving ambiguous file references

When the user refers to "the file" or "this script" without naming it, resolve against workspace state, not against a project-wide search:

1. **Active document.** Call `document_get_state` and check `activeDocument`.
2. **Other open documents.** Same call, check `openDocuments`.
3. **Explorer selection.** Call `explorer_get_state` and check selected resource(s) and expanded folders.

Only after these don't resolve, fall back to `file_grep` or `file_get_tree`. Searching the whole project for an ambiguous reference burns time and risks acting on the wrong file.
