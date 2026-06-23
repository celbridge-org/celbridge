# Workspace panels

A loaded project arranges the UI around a central editor area. You can highlight any named part of this UI for the user with `app_spotlight` (which lists the landmark names); prefer that over describing locations in prose when onboarding or answering "where is X?".

- **Explorer** (left sidebar) — the project file tree, with toolbar buttons to add a file, add a folder, and open project settings. `explorer_*` tools create, move, rename, and delete resources; `explorer_undo` / `explorer_redo` reverse file system operations only, not document text edits.
- **Documents** (centre) — the editor area. Files open as tabs across up to three sections (sectionIndex 0, 1, 2 from left to right); a split-editor button on the document toolbar sets the section count. `document_*` tools open, close, activate, and inspect tabs; `file_*` tools edit content.
- **Inspector** (right sidebar) — contextual properties for the selected resource.
- **Search** — full-text search, reached from the activity bar alongside Explorer in the left sidebar. From the agent, use `file_grep` for the same purpose.
- **Console** (bottom) — a Python REPL where the user converses with you; it can be maximised to fill the editor area.

The sidebars and console are shown or hidden from the title-bar toggle buttons, and the activity bar switches the left sidebar between Explorer and Search. `app_get_state` reports which panels are currently visible and focused.

## Resolving ambiguous file references

When the user refers to "the file" or "this script" without naming it, resolve against workspace state, not against a project-wide search:

1. **Active document.** Call `document_get_state` and check `activeDocument`.
2. **Other open documents.** Same call, check `openDocuments`.
3. **Explorer selection.** Call `explorer_get_state` and check selected resource(s) and expanded folders.

Only after these don't resolve, fall back to `file_grep` or `file_get_tree`. Searching the whole project for an ambiguous reference burns time and risks acting on the wrong file.
