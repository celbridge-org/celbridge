# app_list_utilities

Lists every utility the app can show: the built-in Utility Panel surfaces (Explorer, Search) and any custom utilities. Use it to discover what utilities exist and their ids before calling `app_show_utility`, and to see how each is currently presented and which one the user is looking at.

`app_get_state` reports only which single utility is currently active in the Utility Panel rail; this tool is the full catalog.

## When to call it

Before `app_show_utility`, to learn the valid ids. Also when the user asks what a project offers, or which panels are available: custom utilities vary per project, so this list is not fixed.

## Returns

A JSON object with one field:

- `utilities` (array) — every available utility. Each entry has:
  - `utilityId` (string) — the id to pass to `app_show_utility` (e.g. `celbridge.explorer`, or `{packageName}.{documentId}` for a custom one).
  - `displayName` (string) — the human-readable, localized name.
  - `location` (string) — the utility's current dock location: `"panel"` when it is a rail surface in the Utility Panel, or `"document"` when the user has docked it into a document tab. A utility can move between the two at runtime; the built-in Explorer and Search are always `"panel"`.
  - `isShown` (bool) — whether the utility is currently surfaced to the user: for a `panel`, whether it is the active rail tab; for a `document`, whether its tab is the active document.

Returns an empty `utilities` array when no project is loaded.
