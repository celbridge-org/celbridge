# package_archive

Creates a zip archive from a file or folder inside the project. When archiving a folder, the archive contains the folder's *contents* at the root, not the folder itself — extracting it back over an empty folder reproduces the original layout.

This is the general-purpose zip tool; it is not limited to Celbridge packages. For the package publish/install round trip, prefer `package_publish` and `package_install`.

## Parameters

### resource

Resource key of the file or folder to archive. See `resource_keys`.

### archive

Resource key for the output zip file. The parent folder must already exist.

### include

Optional semicolon-separated glob patterns. When set, only matching files are added (e.g. `"*.py;*.md"`). When empty, all files are included.

### exclude

Optional semicolon-separated glob patterns to skip (e.g. `"__pycache__;.git"`). Applied after `include`.

### overwrite

When `false` (default), the call fails if the archive resource already exists. Set to `true` to replace it.

## Returns

A JSON object:

- `entries` (int) — number of files written into the zip.
- `size` (long) — archive size in bytes.
- `archive` (string) — resource key of the created archive.

## See also

- `package_unarchive` — extract a zip resource back into the project.
- `package_publish` — package-specific upload that handles the registry workflow end-to-end.
- `packages_overview` — the packages folder layout and registry workflow.
