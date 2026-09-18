# app_get_state

Returns application-level state as a JSON object. Most workspace tools require a loaded project, so calling this first lets the agent confirm the session is ready and pick up the information needed to follow the user's attention.

## When to call it

Early in a session, before any project-scoped work. An agent's first tool call in a session also carries a snapshot of this state, which is not refreshed, so call `app_get_state` when current values matter.

## Returns

A JSON object with these fields:

- `version` (string) — the running app's version, in the three-part `MAJOR.MINOR.PATCH` form.
- `configuration` (string) — the build configuration, `Debug` or `Release`. The test-automation tools `app_answer_dialog` and `app_simulate_input` work only in a `Debug` build.
- `isLoaded` (bool) — whether a project is currently loaded.
- `projectName` (string) — the project name, empty when no project is loaded.
- `packages` (array) — each project package as the project loaded, with its `name` and `packageVersion`. Bundled packages are omitted, and the list is empty when no project is loaded. `app_list_packages` reports the same packages with their folders.
- `packageLoadFailureCount` (int) — the number of packages in the project tree that failed to load. `app_list_packages` gives the reasons.
- `featureFlags` (object) — maps every declared flag name to its enabled state for the loaded project. Consult before calling a feature-gated tool.
- `focusedPanel` (string) — the currently focused workspace panel: `Documents`, `Explorer`, `Search`, `CustomUtility` (a contributed utility shown in the Utility Panel), or `None`.
- `activeUtility` (string) — the id of the item selected in the Utility Panel, empty when no project is loaded.
- `layoutMode` (object) — `{areaVisibility}`, which maps each workspace area token (`utility`, `main`, `bottom`, `side`) to whether that area is on screen. In the Default layout `main` is always `true`. Focus and Presentation show only the active document's area, which can be `bottom` or `side`, and report every other area as `false`. Area visibility is stored per project, so a project that has not customised its layout reports the workspace defaults rather than any global preference.
- `spotlightLandmarks` (array) — the landmark names `app_spotlight` accepts, sorted.

Python dependencies are declared per console: read a `.console` file directly with `file_read` — the `[session.python].dependencies` array carries that console's list.
