# app_list_utilities

Lists every utility the app can show: the built-in Utility Panel items (Explorer, Search), the built-in document shortcuts (Project Settings, Community Workshop), and any custom utilities the project contributes. Use it to discover what utilities exist and their ids before calling `app_show_utility`, and to see how each is currently presented and which one the user is looking at.

The list is the utility rail as the user sees it: one entry per rail button. Some of those buttons open a document rather than showing something in the panel, and they are listed all the same, because a user pointing at a button expects you to find it.

`app_get_state` reports only which single utility is currently active in the Utility Panel rail; this tool is the full catalog.

## When to call it

Before `app_show_utility`, to learn the valid ids. Also when the user asks what a project offers, or which panels are available: custom utilities vary per project, so this list is not fixed.

It is also how you learn that a generated document exists. The Workshop's document lives under the `temp:` root, which the resource tree does not enumerate, so its `resource` is discoverable here and nowhere else.

## Returns

A JSON object with one field:

- `utilities` (array) — every available utility. Each entry has:
  - `utilityId` (string) — the id to pass to `app_show_utility` (e.g. `celbridge.explorer`, or the editor id of a contributed utility).
  - `displayName` (string) — the human-readable, localized name.
  - `currentArea` (string) — the workspace area the utility occupies right now: `"utility"` when the Utility Panel shows it, or a document area token (`"main"`, `"bottom"`, `"side"`) when it is a document tab. Empty when nothing presents it, which is a document shortcut whose document is closed. A custom utility moves between areas at runtime; Explorer and Search are always `"utility"`.
  - `dockArea` (string) — the area token this utility opens in as a document, which is where `app_show_utility` sends it when asked for `"document"`. This is what the utility declares, not where it is now: read `currentArea` for that. Empty for a utility that stays in the Utility Panel and cannot become a document, which is what Explorer and Search are.
  - `isVisible` (bool) — whether the user can currently see the utility. That means it is the selected rail item (in the `utility` area) or the active document (in a document area), **and** that its area is not collapsed. A utility can be the selected rail item while the Utility Panel is collapsed, and that reports `false`, because nothing is on screen. Trust this rather than inferring visibility from `currentArea`.
  - `resource` (string) — the file the utility presents, or empty when it has none. Explorer and Search have no file behind them, so they report an empty string.

Returns an empty `utilities` array when no project is loaded.
