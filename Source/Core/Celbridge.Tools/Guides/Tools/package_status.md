# package_status

Reports the project's package state, read locally — it does not contact the workshop. For each discovered project package it returns the name, the installed version, and the folder it lives in; it also lists any packages that failed to load, with the reason. With packages installable anywhere under the project (not just `packages/`), this is how an agent learns what is installed where before choosing an install destination or repairing a duplicate-name fault.

Only project packages are reported. Bundled packages that ship inside the application are not part of the project's state and are omitted, as are copies installed to non-loading roots such as `temp:` (those never load).

## Parameters

None.

## Returns

A JSON object with two arrays:

- `packages` — each loaded project package: `name`, `version` (read from the package's `HISTORY.md`; `null` when the package was hand-authored and never installed from a workshop, so no history exists), and `folder` (the resource key of the package folder, e.g. `project:packages/my-widget`).
- `failures` — each manifest that failed to load: `name` (may be `null` when the manifest could not be parsed), `folder` (resource key), `reason` (e.g. `DuplicateName`, `InvalidManifest`, `ReservedNamePrefix`, `UnregisteredNamespace`, `ReservedExtension`), and an optional `detail`.

## Gotchas

- **A `DuplicateName` failure means two manifests claim the same name, and all of them are skipped** — none loads until the conflict is resolved. Move, rename, or remove one of the colliding folders, then reload the project.
- **`version` reflects the installed/published version recorded in `HISTORY.md`**, not a manifest field — the manifest carries no authoritative version under the workshop model. A `null` version is normal for a package authored in place.
- The reported state is from the last project load; install or publish actions taken in this session are reflected after the project re-scans.
