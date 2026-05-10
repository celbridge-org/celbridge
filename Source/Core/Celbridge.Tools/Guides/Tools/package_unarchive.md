# package_unarchive

Extracts a zip archive that lives somewhere under the project content root into a destination folder. The general-purpose counterpart to `package_archive`; use `package_install` instead when the goal is to install a package from the remote registry.

## Parameters

- `archive` — resource key of the zip file to extract.
- `destination` — resource key of the target folder. Created if it does not exist.
- `overwrite` — when `false` (default), an entry that would clobber an existing file causes the call to fail. Set to `true` to replace existing files.

## Returns

A JSON object:

- `entries` (int) — number of files extracted.
- `destination` (string) — resource key of the destination folder.
