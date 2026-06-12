# package_install

Downloads the latest version of a package from the workshop and extracts it into `packages/{packageName}/`. The package must already exist on the workshop — use `package_list` to discover what is published. By default surfaces a confirmation dialog before installing; pass `confirmWithUser: false` only when the user has explicitly asked for unattended operation.

If a package folder of the same name already exists, the install fails (the underlying unarchive runs with `overwrite: false`). Remove or rename the existing folder first if you intend to replace it.

## Parameters

### packageName

The name as published on the workshop (lowercase alphanumeric with single hyphen separators, 1-64 characters).

### confirmWithUser

When `true` (default), shows a confirmation dialog before downloading and extracting. When `false`, runs silently. Leave at the default unless the user has asked for an unattended run.

## Returns

A JSON object:

- `packageName` (string) — echoed package name.
- `entries` (int) — number of files extracted.
- `destination` (string) — resource key of the destination folder.

## Gotchas

- The downloaded zip is staged briefly under `temp:` and removed after extraction. A failure mid-extract still cleans up the temp file.
- An existing `packages/{packageName}` folder causes the call to fail — decide whether to remove it explicitly rather than relying on a flag.
- A package whose versions have all been tombstoned has no live version and cannot be installed.
