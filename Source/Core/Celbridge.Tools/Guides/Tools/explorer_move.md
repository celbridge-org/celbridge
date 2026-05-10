# explorer_move

Moves a single resource (file or folder) to a new location in the project tree. The original is removed. This is also the silent rename path — pass a destination with a different name in the same parent folder to rename. Folder moves are recursive. The move is recorded on the explorer undo stack.

## destinationResource resolution

Resolved against the source:

- If `destinationResource` names an existing folder, the source is moved **into** that folder, keeping its original name (`Notes/todo.md` to `Archive` becomes `Archive/todo.md`).
- Otherwise the destination is treated as the new full path and name (`Notes/todo.md` to `Notes/done.md` renames the file in place).

## Returns

`"ok"` on success.

## Gotchas

- Moving the document currently open in the editor updates the tab to point at the new path; the tab does not close.
- Renaming a folder that contains open documents updates each open tab's resource path automatically.
