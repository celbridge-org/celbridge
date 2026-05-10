# explorer_create_folder

Creates an empty folder in the project tree. Use the silent form to script directory layout, or the dialog form to let the user name the folder.

## Parameters

### resource

Resource key of the folder to create. When `showDialog` is `true`, this is interpreted as the **parent folder** to preselect in the dialog rather than the new folder's own key.

### showDialog

When `false` (the default), the folder is created silently at `resource`. When `true`, the create-folder dialog opens with `resource` as the preselected parent folder and the user types the name.

## Returns

`"ok"` on success. The creation is recorded on the explorer undo stack.
