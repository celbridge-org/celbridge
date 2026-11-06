# explorer_redo

Re-applies the most recently undone explorer operation (file or folder create, delete, move, rename, copy). Only operations that went through the explorer undo stack are eligible — text edits and spreadsheet edits live on different stacks.

## Returns

`"ok"` on success.

An error when there is nothing to redo.

An error when the operation can't run again, for example when the source of a copy has gone. The entry stays on the redo stack, so calling `explorer_redo` again returns the same error until the cause clears.
