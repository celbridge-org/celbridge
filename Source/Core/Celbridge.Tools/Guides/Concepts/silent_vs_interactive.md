# Silent vs. interactive tools

Most tools run silently — they execute, return a result, and leave no UI trace beyond the state change itself. A small number of tools surface a confirmation dialog or notification to the user.

## Silent by default

- Every `file_*`, `document_*`, `explorer_*`, `spreadsheet_*`, `webview_*`, `query_*`, `docs_*`, `app_log*` tool. These produce a result and (where appropriate) a state change without prompting.

## Interactive by default

- `app_show_alert` always surfaces a modal dialog. There is no silent mode.
- `package_publish` and `package_install` accept `confirmWithUser` (default `true`). When `true`, the application shows a confirmation dialog and the tool waits for the user's response before proceeding. Pass `false` only when the user has explicitly asked for an unattended run (e.g. inside a script they're invoking themselves).

## Why this matters

If the project is loaded but the user has stepped away, an interactive tool blocks until they return. A silent tool with the same effect might be more appropriate — but only when the user has asked for unattended operation. The default of `confirmWithUser: true` is deliberately conservative: a destructive registry operation is the kind of action that should pause for human approval.

When in doubt, leave `confirmWithUser` at its default. The user will tell you when they want unattended behaviour.
