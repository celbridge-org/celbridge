# explorer_select

Selects a resource in the explorer tree. Ancestor folders are expanded as needed so the selection is visible. Use this to direct the user's attention to a specific file or folder after performing work on it.

## Parameters

### resource

Resource key of the file or folder to select. See `resource_keys` for the syntax.

### showExplorerPanel

When `true` (the default), the explorer panel is brought into view if it was hidden. Pass `false` to leave the panel's visibility unchanged.

## Returns

`"ok"` on success.

## See also

- `explorer_get_state` — confirm what is currently selected.
- `explorer_expand_folder`, `explorer_collapse_all` — adjust surrounding tree state.
- `workspace_panels` — focused panel and layout.
