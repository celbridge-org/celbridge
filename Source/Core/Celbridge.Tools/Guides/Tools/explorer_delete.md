# explorer_delete

Deletes a resource from the project tree. Folder deletes are recursive. The delete is recorded on the explorer undo stack and can be reversed with `explorer_undo`, but undo only restores resources that the application itself deleted, so do not rely on it as a substitute for source control.

## showDialog

When `false` (the default), the deletion proceeds silently. When `true`, a confirmation dialog opens and the tool waits for the user to confirm or cancel. Prefer the dialog form when the user has not explicitly approved this deletion in the current turn, especially for folders.

## Returns

`"ok"` on success. If the user cancels the confirmation dialog, the result is still success — nothing happened, and the project is unchanged.

## Gotchas

- A delete that targets the document currently open in the editor closes that tab. Document-level state (Monaco undo history, view position) is lost.
- Programmatic file edits made before the delete cannot be recovered through Monaco's undo, only through `explorer_undo`.
