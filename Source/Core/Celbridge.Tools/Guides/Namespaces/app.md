# app

The `app` namespace covers application-level concerns that are not tied to a specific resource: querying workspace state, logging to the console panel, refreshing the file system view, highlighting UI landmarks for the user, and showing modal alerts. Most workspace tools require a loaded project, so `app_get_state` is typically the first call on a fresh session.

## Must-knows

- **Call `app_get_state` first.** It reports the running app `version`, whether a project is loaded, the feature-flag map (consult before invoking a feature-gated tool such as `webview_eval`), the focused panel, and the layout-mode visibility flags.
- **Point at the UI rather than describing it.** When the user asks where something is, or you are orienting a new user, `app_spotlight` highlights a named landmark (a panel or button) with a callout; an empty target clears it. The `workspace_panels` guide maps the UI and `app_spotlight` lists the landmark names.
- **Logging tools are user-visible.** `app_log`, `app_log_warning`, and `app_log_error` write to the console panel the user can see. Use them for status the user should notice; do not use them for internal trace output.
- **`app_show_alert` is interactive.** It blocks until the user dismisses the dialog. Use it for genuinely modal confirmations, not status updates.
- **`app_refresh_files`** rescans the project content folder. Most file-system mutations performed via tools update the explorer automatically; call this only when an external process has modified files behind the application's back.

## Tools

- `app_get_state` — workspace state snapshot (app version, project load, feature flags, focused panel, layout, registered UI automations).
- `app_log`, `app_log_warning`, `app_log_error` — write a message to the console panel at the named severity.
- `app_refresh_files` — rescan the project's content folder for external changes.
- `app_show_alert` — show a modal alert dialog and wait for the user to dismiss it.
- `app_spotlight` — highlight a named UI landmark with a callout to show the user where it is; an empty target clears the current spotlight.
- `app_answer_dialog` *(debug-only; gated by `answer-dialog`)* — schedules an automated answer for the next modal dialog, so a script can drive a flow that would otherwise block on user interaction. Used by integration tests for the always-prompt admin tools (`package_delete`, `package_unpublish`) and the dialog-driven `explorer_rename`. Structurally absent from release builds.
