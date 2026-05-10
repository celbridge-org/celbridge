# explorer_create_file

Creates an empty file in the project. For files that should start with content, prefer `file_write` — it creates the file and writes the content in one step. Use this tool when you specifically need an empty file, or when the user should pick the name interactively.

## Parameters

### resource

Resource key of the file to create. When `showDialog` is `true`, this is interpreted as the **parent folder** to preselect in the dialog rather than the new file's own key.

### showDialog

When `false` (the default), the file is created silently at `resource`. When `true`, the create-file dialog opens with `resource` as the preselected parent folder and the user types the name.

## Returns

`"ok"` on success. The creation is recorded on the explorer undo stack.
