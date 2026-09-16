# app_list_packages

Reports the project's packages as the project loaded. For each project package it returns the name, the package version its manifest declares, and the folder it lives in. It also lists the packages that failed to load, with the reason. With packages installable anywhere under the project (not just `packages/`), this is how an agent learns what is installed where before choosing an install destination or repairing a duplicate-name fault.

Only project packages are reported. Bundled packages that ship inside the application are not part of the project's state and are omitted, as are copies under non-loading roots such as `temp:`, which never load. `app_get_state` carries the same packages as a summary, with the number of load failures.

## Returns

A JSON object with two arrays:

- `packages` — each loaded project package: `name`, `packageVersion` (the manifest's `package-version`, or `1.0.0` when it sets none), and `folder` (the resource key of the package folder, e.g. `project:packages/my-widget`).
- `failures` — each manifest in the project tree that failed to load: `name` (may be `null` when the manifest could not be parsed), `folder` (resource key), `reason` (`InvalidManifest`, `DuplicateName`, `ReservedNamePrefix` or `ReservedExtension`), and an optional `detail`.

## Gotchas

- **The list is fixed when the project loads.** A package added, removed or edited on disk during the session is not reflected until the project reloads, and the load report then lists any failures. To see what a project declares right now, read its `package.toml` files and its `.celbridge` file.
- **A `DuplicateName` failure means two manifests claim the same name, and all of them are skipped.** None loads until the conflict is resolved. Move, rename, or remove one of the colliding folders, then reload the project.
- **`packageVersion` comes from the manifest**, not from `HISTORY.md`, so it is the version the package declares rather than the workshop version it was installed from. A malformed `package-version` fails the package with `InvalidManifest`, and `detail` quotes the value.
