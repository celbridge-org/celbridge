# package_status

Reports the project's package state, read locally — it does not contact the workshop. For each discovered project package it returns the name, the installed version, and the folder it lives in; it also lists any packages that failed to load, with the reason. With packages installable anywhere under the project (not just `packages/`), this is how an agent learns what is installed where before choosing an install destination or repairing a duplicate-name fault.

Only project packages are reported. Bundled packages that ship inside the application are not part of the project's state and are omitted, as are copies installed to non-loading roots such as `temp:` (those never load).

## Parameters

### refresh (optional, default `false`)

Re-run package discovery against the on-disk state before returning. Set this to `true` when an agent installed or removed a package in the current session and wants to see it reflected without a full project reload. A refresh re-reads every project `package.toml` and updates the load-failure list (so a freshly-introduced duplicate-name fault becomes visible); editor-contribution registration is workspace-load-scoped and is **not** refreshed — packages contributing new editors still need a reload before those editors become available.

The default is `false` to keep the typical read cheap: a refresh walks the project's visible resource set.

## Returns

A JSON object with two arrays:

- `packages` — each loaded project package: `name`, `version` (read from the package's `HISTORY.md`; `null` when the package was hand-authored and never installed from a workshop, so no history exists), and `folder` (the resource key of the package folder, e.g. `project:packages/my-widget`).
- `failures` — each manifest that failed to load: `name` (may be `null` when the manifest could not be parsed), `folder` (resource key), `reason` (e.g. `DuplicateName`, `InvalidManifest`, `ReservedNamePrefix`, `UnregisteredNamespace`, `ReservedExtension`), and an optional `detail`.

## Gotchas

- **A `DuplicateName` failure means two manifests claim the same name, and all of them are skipped** — none loads until the conflict is resolved. Move, rename, or remove one of the colliding folders, then reload the project.
- **`version` reflects the installed/published version recorded in `HISTORY.md`**, not a manifest field — the manifest carries no authoritative version under the workshop model. A `null` version is normal for a package authored in place.
- Without `refresh: true`, the reported state is from the last project load. Pass `refresh: true` after a session-mid install / uninstall / manifest edit to see it. A full project reload is still required for packages contributing new document editors to become usable.
