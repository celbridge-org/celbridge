# explorer_rename

Shows the rename dialog for a file or folder. The dialog opens preseeded with the current name and the user types the new name. Use `explorer_move` to rename silently to a known new name without surfacing UI.

The rename runs the same cascade as `explorer_move` — references to the renamed resource are rewritten in place across the project, and a paired `.cel` sidecar moves alongside its parent. Use `explorer_move` instead of this dialog when you need the structured response (skipped referencers, partial failures) — the dialog form returns only `"ok"` or cancel.

## Returns

`"ok"` on success. If the user cancels the dialog, the result is still success and nothing is renamed.
