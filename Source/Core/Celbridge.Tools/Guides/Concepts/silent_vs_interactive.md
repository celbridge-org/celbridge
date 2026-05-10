# Silent vs. interactive tools

Most tools run silently — they execute, return a result, and leave no UI trace beyond the state change itself. A small number surface a confirmation dialog or notification.

## Silent by default

Every `file_*`, `document_*`, `explorer_*`, `spreadsheet_*`, `webview_*`, `query_*`, `docs_*`, and `app_log*` tool. These produce a result and (where appropriate) a state change without prompting.

## Interactive by default

- `app_show_alert` always surfaces a modal dialog. There is no silent mode.
- `package_publish`, `package_install`, `explorer_rename`, `explorer_duplicate`, and the dialog forms of `explorer_create_file` / `explorer_create_folder` / `explorer_delete` accept `confirmWithUser` or `showDialog`. When set, the application shows a dialog and the tool waits for the user's response.

## Why this matters

If the user has stepped away, an interactive tool blocks until they return. The default is conservative — destructive registry operations are the kind of action that should pause for human approval. Pass `false` only when the user has explicitly asked for unattended operation.
