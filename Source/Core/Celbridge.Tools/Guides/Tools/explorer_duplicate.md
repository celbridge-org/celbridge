---
name: explorer_duplicate
description: Duplicate a resource alongside the original; always shows the rename dialog so the user picks the new name.
---

# explorer_duplicate

Creates a copy of a resource alongside the original. This tool is always interactive — the rename dialog opens preseeded with a default name, and the user confirms or types a different one before the copy is committed. Use `explorer_copy` for a silent copy to a known destination.

## Parameters

### resource

Resource key of the file or folder to duplicate. See `resource_keys` for the syntax.

## Returns

`"ok"` on success. If the user cancels the dialog, the result is still success and nothing is copied.

## See also

- `explorer_copy` — silent copy to a chosen destination.
- `explorer_rename` — interactive rename, same dialog flow.
- `resource_keys`, `silent_vs_interactive`, `undo_semantics`.
