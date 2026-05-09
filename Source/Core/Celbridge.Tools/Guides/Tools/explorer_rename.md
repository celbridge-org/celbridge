# explorer_rename

Shows the rename dialog for a file or folder. The dialog opens preseeded with the current name and the user types the new name. Use `explorer_move` when you need to rename silently to a known new name without surfacing UI.

## Parameters

### resource

Resource key of the file or folder to rename. See `resource_keys` for the syntax.

## Returns

`"ok"` on success. If the user cancels the dialog, the result is still success and nothing is renamed.

## See also

- `explorer_move` — silent rename by writing to a new destination key.
- `explorer_duplicate` — interactive copy via the same dialog flow.
- `resource_keys`, `silent_vs_interactive`, `undo_semantics`.
