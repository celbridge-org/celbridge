# explorer_undo

Reverses the most recent explorer file-system operation. The explorer undo stack is independent of the editor and spreadsheet undo stacks — read `undo_semantics` before relying on this to recover work, especially after programmatic file edits.

## Returns

`"ok"` on success.

An error when there is nothing to undo.

An error when undo refuses to remove an item. Undo refuses when a file or folder has changed. Undo also refuses when the source of a copy or import has changed or gone. The error names the item, nothing changes, and the entry stays on the stack. Calling `explorer_undo` again returns the same error, so older operations stay out of reach. See `undo_semantics`.
