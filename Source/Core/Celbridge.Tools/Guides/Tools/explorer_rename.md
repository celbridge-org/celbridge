# explorer_rename

Shows the rename dialog for a file or folder. The dialog opens preseeded with the current name and the user types the new name. Use `explorer_move` to rename silently to a known new name without surfacing UI.

## Returns

`"ok"` on success. If the user cancels the dialog, the result is still success and nothing is renamed.
