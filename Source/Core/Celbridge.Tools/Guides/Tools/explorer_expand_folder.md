# explorer_expand_folder

Sets the expanded or collapsed state of a folder in the explorer panel. Pair with `explorer_select` when guiding the user toward a specific resource — expand the parent first so the selection is visible without scrolling.

## expanded

When `true` (the default), the folder is expanded. When `false`, it is collapsed. Calling with the current state is a no-op.

## Returns

`"ok"` on success.
