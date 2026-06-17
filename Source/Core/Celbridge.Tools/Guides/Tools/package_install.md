# package_install

Downloads a package from the connected workshop and extracts it into a folder named for the package under a destination of your choosing (default `packages/`). Use `package_list` to discover what is published and `package_info` to see the versions and aliases a package offers. By default a confirmation dialog is shown before installing; pass `confirmWithUser: false` only when the user has explicitly asked for unattended operation.

## Parameters

### packageName

The name as published on the workshop (lowercase alphanumeric with single hyphen separators, 1-64 characters).

### version

Which version to install. Accepts a version number (e.g. `3`), an alias name (e.g. `stable`), or `latest` (the default), which selects the highest live version. A deleted version cannot be installed: a number or alias resolves to its target, but the download then reports that the version has been deleted. `latest` always skips deleted versions.

### destination

Resource key of the folder the package is installed *into*. The package always lands in a `{packageName}` subfolder of this destination, so two packages never overlap. Defaults to `packages/` in the project root. Any writeable root works — `packages`, `project:lib`, or a staging area such as `temp:package-staging/review`. Only packages under the `project:` root load as features; copies under other roots are inert reference data for comparison and merge workflows.

### confirmWithUser

When `true` (default), shows a confirmation dialog before downloading and extracting. When the destination already holds the package, the prompt names the folder, states that local changes will be lost, and shows the installed and incoming versions. Leave at the default unless the user has asked for an unattended run.

## Returns

A JSON object:

- `packageName` (string) — echoed package name.
- `version` (int) — the version number that was installed.
- `entries` (int) — number of files extracted.
- `destination` (string) — resource key of the package folder.

## Reinstalling replaces

Installing over an existing package folder completely replaces its contents — there is no merge. The replaced files are moved to the resource trash first, so even a silent reinstall is recoverable with undo. The installed version's workshop history is written to `HISTORY.md` beside the manifest (newest first); that file is how the installed version is later determined.

## Gotchas

- Installing into `project:` fails before downloading if another manifest already claims the same package name at a *different* path — move, rename, or remove it first, or reinstall over the existing folder to replace it. Use `package_status` to see what is installed where. Copies under non-loading roots (e.g. `temp:`) are exempt because they never load.
- The downloaded zip is staged briefly under `temp:` and removed after extraction, even if the extract fails partway.
- A package whose versions have all been deleted has no live version, so `latest` cannot resolve and the install fails.
- `HISTORY.md` is generated metadata, not package content; `package_publish` never uploads it.
