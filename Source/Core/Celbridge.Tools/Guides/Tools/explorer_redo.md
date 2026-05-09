# explorer_redo

Re-applies the most recently undone explorer operation (file or folder create, delete, move, rename, copy). Only operations that went through the explorer undo stack are eligible — text edits and spreadsheet edits live on different stacks. See `undo_semantics` for the full picture.

## Returns

`"ok"` on success. If there is nothing to redo, the result is still success and nothing changes.

## See also

- `explorer_undo` — the inverse.
- `undo_semantics` — which actions go on which stack.
