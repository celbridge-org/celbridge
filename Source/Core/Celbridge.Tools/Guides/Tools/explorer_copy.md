# explorer_copy

Copies a single resource (file or folder) to a new location in the project tree. The original resource is left in place. Folder copies are recursive. The copy is recorded on the explorer undo stack and can be reversed with `explorer_undo`.

## destinationResource resolution

Resolved against the source:

- If `destinationResource` names an existing folder, the source is copied **into** that folder, keeping its original name (`Notes/todo.md` to `Archive` becomes `Archive/todo.md`).
- Otherwise the destination is treated as the new full path and name (`Notes/todo.md` to `Archive/old-todo.md` produces that file directly).

## Returns

`"ok"` on success. On any failure the destination is not created and an error is returned.
