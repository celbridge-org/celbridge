# Undo semantics

Celbridge has three independent undo stacks, each scoped to a different surface.

## Explorer undo

`explorer_undo` and `explorer_redo` reverse **file-system operations**: `explorer_create_file`, `explorer_create_folder`, `explorer_delete`, `explorer_move`, `explorer_rename`, `explorer_copy`. The stack is per-session; closing the project clears it.

Explorer undo cannot reverse:

- Document text edits — different stack.
- Spreadsheet cell edits — different stack.
- Anything outside the project tree.

## Editor (Monaco) undo

The text editor maintains its own undo history per open document. `Ctrl+Z` reverses recent typing, paste, find-replace, etc.

**Programmatic edits wipe Monaco's undo history** for the open document. `file_apply_edits`, `file_write`, `file_find_replace`, `file_delete_lines`, and `file_write_binary` write straight to disk; if the document is open, the buffer reloads and the undo stack is cleared. The agent's edit cannot be reverted with Ctrl+Z. To undo an agent edit, apply a reverse edit with `file_apply_edits` or `file_delete_lines`.

## Spreadsheet editor undo

The SpreadJS editor maintains its own undo history for cells edited via the editor UI. Programmatic spreadsheet writes trigger an external-reload path on an open workbook, which wipes the editor's undo history for that workbook. Agents that need to recover a pre-edit value should `spreadsheet_read_sheet` before writing.

## What never has undo

- Git or filesystem operations outside the project.
- Tool calls that operate on external systems (e.g. publishing a package — destructive, gated by `confirmWithUser`).
- Application state changes (focus, layout) — user-recoverable but not on an undo stack.
