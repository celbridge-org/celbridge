---
name: explorer_undo
description: Reverse the most recent explorer operation (create, delete, move, rename, copy); does not affect text or spreadsheet edits.
---

# explorer_undo

Reverses the most recent explorer file-system operation. The explorer undo stack is independent of the editor and spreadsheet undo stacks — read `undo_semantics` before relying on this to recover work, especially after programmatic file edits.

## Returns

`"ok"` on success. If there is nothing to undo, the result is still success and nothing changes.

## See also

- `explorer_redo` — re-apply the most recently undone operation.
- `undo_semantics` — which actions go on which stack and what is unrecoverable.
