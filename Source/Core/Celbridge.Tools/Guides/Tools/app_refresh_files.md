# app_refresh_files

Forces a rescan of the project file listing. The application normally watches the project tree and updates its in-memory listing automatically, so this is rarely needed.

Only call when a non-Celbridge tool (a separate MCP server, a bash command, a script outside the application) wrote files directly into the project tree and the listing has not picked up the change. Celbridge's own `file_*` and `explorer_*` tools update the listing themselves.
