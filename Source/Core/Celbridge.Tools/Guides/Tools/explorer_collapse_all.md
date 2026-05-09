# explorer_collapse_all

Collapses every currently expanded folder in the explorer panel, leaving only the project root visible. Use this to reset a cluttered tree before directing the user's attention somewhere specific (typically followed by `explorer_select` or `explorer_expand_folder`).

## Returns

`"ok"` on success.

## See also

- `explorer_expand_folder` — expand or collapse a single folder.
- `explorer_get_state` — inspect which folders are currently expanded.
