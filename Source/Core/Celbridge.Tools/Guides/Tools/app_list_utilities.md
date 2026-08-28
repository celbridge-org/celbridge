# app_list_utilities

Lists every utility the app can show: the built-in Utility Panel surfaces (Explorer, Search), the built-in launchers (Project Settings, Community Workshop), and any custom utilities the project contributes. Use it to discover what utilities exist and their ids before calling `app_show_utility`, and to see how each is currently presented and which one the user is looking at.

The list is the utility rail as the user sees it: one entry per rail button. Some of those buttons open a document rather than a panel surface, and they are listed all the same, because a user pointing at a button expects you to find it.

`app_get_state` reports only which single utility is currently active in the Utility Panel rail; this tool is the full catalog.

## When to call it

Before `app_show_utility`, to learn the valid ids. Also when the user asks what a project offers, or which panels are available: custom utilities vary per project, so this list is not fixed.

It is also how you learn that a generated document exists. The Workshop's document lives under the `temp:` root, which the resource tree does not enumerate, so its `resource` is discoverable here and nowhere else.

## Returns

A JSON object with one field:

- `utilities` (array) — every available utility. Each entry has:
  - `utilityId` (string) — the id to pass to `app_show_utility` (e.g. `celbridge.explorer`, or the editor id of a contributed utility).
  - `displayName` (string) — the human-readable, localized name.
  - `area` (string) — the workspace area the utility occupies: `"utility"` when it is a rail surface in the Utility Panel, or a document area token (`"main"`, `"bottom"`, `"side"`) when it is a document tab. A custom utility can move between the areas it allows at runtime; Explorer and Search are always `"utility"`, and the launchers are always `"main"`.
  - `allowedAreas` (array of string) — the areas this utility may be moved to, which `area` is always one of. Pass one of these to `app_show_utility`; any other area is refused. Explorer and Search report `["utility"]` and the launchers report `["main"]`, so a single-entry list means the utility cannot be moved. A list containing `"utility"` also marks the entry as one that lives in the rail and is never destroyed; one without it is a button that opens an ordinary document.
  - `isShown` (bool) — whether the utility is currently surfaced to the user: in the `utility` area, whether it is the active rail tab; in a document area, whether its tab is the active document.
  - `resource` (string) — the file the utility presents, or empty when it has none. Explorer and Search have no file behind them, so they report an empty string.

Returns an empty `utilities` array when no project is loaded.
