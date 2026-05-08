---
name: explorer_create_folder
description: Create an empty folder at a given resource key, or open the create-folder dialog with the parent folder preselected.
---

# explorer_create_folder

Creates an empty folder in the project tree. Use the silent form to script directory layout, or the dialog form to let the user name the folder.

## Parameters

### resource

Resource key of the folder to create. When `showDialog` is `true`, this is interpreted as the **parent folder** to preselect in the dialog rather than the new folder's own key. See `resource_keys` for the syntax.

### showDialog

When `false` (the default), the folder is created silently at `resource`. When `true`, the create-folder dialog opens with `resource` as the preselected parent folder and the user types the name. See `silent_vs_interactive` for the trade-off.

## Returns

`"ok"` on success. The creation is recorded on the explorer undo stack and can be reversed with `explorer_undo`.

## See also

- `explorer_create_file` — same shape, for files.
- `explorer_undo` — reverse the creation.
- `resource_keys`, `silent_vs_interactive`, `undo_semantics`.
