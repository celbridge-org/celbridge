---
name: explorer_create_file
description: Create an empty file at a given resource key, or open the create-file dialog with the parent folder preselected.
---

# explorer_create_file

Creates an empty file in the project. For files that should start with content, prefer `file_write` — it creates the file and writes the content in one step. Use this tool when you specifically need an empty file, or when the user should pick the name interactively.

## Parameters

### resource

Resource key of the file to create. When `showDialog` is `true`, this is interpreted as the **parent folder** to preselect in the dialog rather than the new file's own key. See `resource_keys` for the syntax.

### showDialog

When `false` (the default), the file is created silently at `resource`. When `true`, the create-file dialog opens with `resource` as the preselected parent folder and the user types the name. See `silent_vs_interactive` for the trade-off.

## Returns

`"ok"` on success. The creation is recorded on the explorer undo stack and can be reversed with `explorer_undo`.

## See also

- `file_write` — create a file with content in a single call.
- `explorer_create_folder` — same shape, for folders.
- `explorer_undo` — reverse the creation.
- `resource_keys`, `silent_vs_interactive`, `undo_semantics`.
