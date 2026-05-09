# app_get_state

Returns application-level state as a JSON object. Most workspace tools require a loaded project, so calling this first lets the agent confirm the session is ready and pick up the information needed to follow the user's attention.

## When to call it

Early in a session, before any project-scoped work. The response also names the orientation guide via `agentDocs.entry`, so an agent that doesn't yet know the guide library can discover it through `app_get_state`.

## Returns

A JSON object with these fields:

- `isLoaded` (bool) — whether a project is currently loaded.
- `projectName` (string) — the project name, empty when no project is loaded.
- `featureFlags` (object) — maps each public flag name to its enabled state. Consult before calling a feature-gated tool. Currently includes `webview-dev-tools` and `webview-dev-tools-eval`.
- `agentDocs` (object) — `{entry, via}`. The entry is the orientation guide name (`agent_instructions`); `via` names the tool to read it through (`guides_read`). Compliant agents call `guides_read([entry])` on a fresh session — the broker's cold-start gate also enforces this for agent connections.
- `focusedPanel` (string) — the currently focused workspace panel (`Documents`, `Explorer`, `Inspector`, `Console`, etc., or `None`).
- `layoutMode` (object) — `{contextPanelVisible, inspectorPanelVisible, consolePanelVisible, consoleMaximized}`. Tells you which regions the user can see right now.

To inspect the project's declared Python dependencies, read the `.celbridge` project file directly with `file_read` — the `[project].dependencies` array carries the list.

## Feature flags

Always check the relevant flag before invoking a gated tool. For example, `webview_eval` requires both `webview-dev-tools` and `webview-dev-tools-eval`; if either is off, tell the user which flag is gating the action and why.

## See also

- `agent_instructions` — the orientation guide named by `agentDocs.entry`.
- `workspace_panels` — what `focusedPanel` and `layoutMode` mean.
- `document_get_state`, `explorer_get_state` — finer-grained state for what the user is currently viewing or selecting.
