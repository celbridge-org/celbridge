---
name: app_get_version
description: Returns the running Celbridge build version as a "major.minor.patch" string.
---

# app_get_version

Returns the running Celbridge application version. Useful when reporting a problem to the user or branching behaviour on a known release.

## Returns

A version string of the form `"major.minor.patch"`, e.g. `"0.2.5"`. The result is the raw string body, not a JSON object.

## See also

- `app_get_state` — broader application state (project, feature flags, layout).
