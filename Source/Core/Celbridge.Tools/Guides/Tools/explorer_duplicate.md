# explorer_duplicate

Creates a copy of a resource alongside the original. Always interactive — the rename dialog opens preseeded with a default name, and the user confirms or types a different one before the copy is committed. Use `explorer_copy` for a silent copy to a known destination.

## Returns

`"ok"` on success. If the user cancels the dialog, the result is still success and nothing is copied.
