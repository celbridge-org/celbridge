---
name: explorer_expand_folder
description: Expand or collapse a single folder in the explorer tree.
---

# explorer_expand_folder

Sets the expanded or collapsed state of a folder in the explorer panel. Pair with `explorer_select` when guiding the user toward a specific resource — expand the parent first so the selection is visible without scrolling.

## Parameters

### resource

Resource key of the folder. See `resource_keys` for the syntax.

### expanded

When `true` (the default), the folder is expanded. When `false`, it is collapsed. Calling with the current state is a no-op.

## Returns

`"ok"` on success.

## See also

- `explorer_collapse_all` — collapse every expanded folder in one call.
- `explorer_select` — focus a resource (auto-expands ancestors).
- `explorer_get_context` — inspect which folders are currently expanded.
