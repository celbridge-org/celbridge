# workshop_install_package

Downloads a package from the connected workshop and extracts it into a folder named for the package under a destination of your choosing (default `packages/`). It installs Celbridge packages, not Python packages, which a `.console` file declares in its `[session.python].dependencies` array. Use `workshop_list_packages` to discover what is published and `workshop_get_package_info` to see the workshop versions and aliases a package offers. By default a confirmation dialog is shown before installing. Pass `confirmWithUser: false` only when the user has explicitly asked for unattended operation.

## Parameters

### packageName

The name as published on the workshop (lowercase alphanumeric with single hyphen separators, 1-64 characters).

### workshopVersion

Which workshop version to install. Accepts a workshop version number (e.g. `3`), an alias name (e.g. `stable`), or `latest` (the default), which selects the highest live workshop version. A deleted workshop version cannot be installed: a number or alias resolves to its target, but the download then reports that it has been deleted. `latest` always skips deleted workshop versions.

### destination

Resource key of the folder the package is installed *into*. The package always lands in a `{packageName}` subfolder of this destination, so two packages never overlap. Defaults to `packages/` in the project root. Any writeable root works, such as `packages`, `project:lib`, or a staging area such as `temp:package-staging/review`. Only packages under the `project:` root load. Copies under other roots are inert reference data for comparison and merge workflows.

### confirmWithUser

When `true` (default), shows a confirmation dialog before downloading and extracting. When the destination already holds the package, the prompt names the folder, states that local changes will be lost, and shows the installed and incoming workshop versions. Leave at the default unless the user has asked for an unattended run.

## Returns

A JSON object:

- `packageName` (string) — echoed package name.
- `workshopVersion` (int) — the workshop version that was installed.
- `entries` (int) — number of files extracted.
- `destination` (string) — resource key of the package folder.

## Reinstalling replaces

Installing over an existing package folder completely replaces its contents, with no merge. The replaced files are moved to the resource trash first, so even a silent reinstall is recoverable with undo. The installed workshop version's history is written to `HISTORY.md` beside the manifest, newest first. The workshop tools read it back to tell which workshop version a folder was installed from, and it has no effect on the manifest's `package-version`.

## Gotchas

- A project must be loaded. Without one, the install fails before contacting the workshop.
- Installing into `project:` fails before downloading if a package the project loaded claims the same name at a *different* path and its manifest is still there. Move, rename, or remove it first, or reinstall over the existing folder to replace it. Use `app_list_packages` to see what is installed where. The check reads the packages as the project loaded, so a same-name copy added during the session is not caught, and shows up as a `DuplicateName` load failure on the next load. Copies under non-loading roots (e.g. `temp:`) are exempt because they never load.
- The downloaded zip is staged briefly under `temp:` and removed after extraction, even if the extract fails partway.
- A package whose workshop versions have all been deleted has no live workshop version, so `latest` cannot resolve and the install fails.
- `HISTORY.md` is generated metadata, not package content, and `workshop_publish_package` never uploads it.
