# app_refresh_files

Forces a rescan of the project file listing. The application normally watches the project tree and updates its in-memory listing automatically, so this is rarely needed.

## When to use it

Only when a non-Celbridge tool (a separate MCP server, a bash command, a script outside the application) wrote files directly into the project tree and the application's listing hasn't picked up the change yet. Celbridge's own `file_*` and `explorer_*` tools update the listing themselves — you should never need to follow them with `app_refresh_files`.

## Returns

`"ok"` on success.

## See also

- `file_changes` — the file save model and how Celbridge's own write tools interact with the listing.
