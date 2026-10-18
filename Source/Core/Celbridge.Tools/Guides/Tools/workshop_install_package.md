# workshop_install_package

Downloads a package from the connected workshop and extracts it into one of the project's package folders (default `packages/`). The package lands in a subfolder named for the package. The tool installs Celbridge packages, not Python packages. A `.console` file declares Python packages in its `[session.python].dependencies` array. Use `workshop_list_packages` to discover what is published and `workshop_get_package_info` to see the workshop versions and aliases a package offers. By default a confirmation dialog is shown before installing. Pass `confirmWithUser: false` only when the user has explicitly asked for unattended operation.

## Parameters

### packageName

The name as published on the workshop (lowercase alphanumeric with single hyphen separators, 1-64 characters).

### workshopVersion

Which workshop version to install. Accepts a workshop version number (e.g. `3`), an alias name (e.g. `stable`), or `latest` (the default), which selects the highest live workshop version. A deleted workshop version cannot be installed: a number or alias resolves to its target, but the download then reports that it has been deleted. `latest` always skips deleted workshop versions.

### destination

Resource key of the package folder to install *into*. The package always lands in a `{packageName}` subfolder of this destination, so two packages never overlap. Defaults to `packages/` in the project root. The destination must be a package folder: `packages/`, or a folder listed in `folders` in the project file's `[celbridge.packages]` table (see `packages_overview`). Any other destination is refused before anything is downloaded, and the error names the package folders. A folder added to the list during the session is refused until the project reloads.

### confirmWithUser

When `true` (default), shows a confirmation dialog before downloading and extracting. When the destination already holds the package, the prompt names the folder, states that local changes will be lost, and shows the installed and incoming workshop versions. Leave at the default unless the user has asked for an unattended run.

## Returns

A JSON object:

- `packageName` (string) — echoed package name.
- `workshopVersion` (int) — the workshop version that was installed.
- `entries` (int) — number of files extracted.
- `destination` (string) — resource key of the installed package's folder, e.g. `project:packages/my-widget`.

## Reinstalling replaces

Installing over an installed package completely replaces the contents of its folder, with no merge. The replaced files are moved to the resource trash first, so even a silent reinstall is recoverable with undo. The installed workshop version's history is written to `HISTORY.md` beside the manifest, newest first. The workshop tools read `HISTORY.md` back to tell which workshop version a folder was installed from. The file has no effect on the manifest's `package-version`.

## Gotchas

- A project must be loaded. Without one, the install fails before contacting the workshop.
- Installing fails before downloading when a loaded package has the same name at a *different* path and its manifest is still there. Move, rename or remove that package first. Reinstalling over the existing folder replaces the package. Use `app_list_packages` to see what is installed where. The check reads the packages that loaded with the project. A same-name copy added during the session passes the check, and the next load reports it as a `DuplicateName` failure.
- The downloaded zip is staged briefly under `temp:` and removed after extraction, even if the extract fails partway.
- A package whose workshop versions have all been deleted has no live workshop version, so `latest` cannot resolve and the install fails.
- `HISTORY.md` is generated metadata, not package content, and `workshop_publish_package` never uploads it.
