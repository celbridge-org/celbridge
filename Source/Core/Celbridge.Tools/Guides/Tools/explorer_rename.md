# explorer_rename

Shows the rename dialog for a file or folder. The dialog opens preseeded with the current name and the user types the new name. Use `explorer_move` to rename silently to a known new name without surfacing UI.

The rename works as `explorer_move` does: a paired `.cel` sidecar moves alongside its parent, and references to the renamed resource in other files still name its old key. Use `explorer_move` instead of this dialog when you need the structured response (partial failures) — the dialog form returns only `"ok"` or cancel.

## Returns

`"ok"` on success. If the user cancels the dialog, the result is still success and nothing is renamed.
