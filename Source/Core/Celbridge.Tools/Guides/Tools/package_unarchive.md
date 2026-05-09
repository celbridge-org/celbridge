# package_unarchive

Extracts a zip archive that lives somewhere under the project content root into a destination folder. The general-purpose counterpart to `package_archive`; use `package_install` instead when the goal is to install a package from the remote registry.

## Parameters

### archive

Resource key of the zip file to extract. See `resource_keys`.

### destination

Resource key of the target folder. Created if it doesn't exist.

### overwrite

When `false` (default), an entry that would clobber an existing file causes the call to fail. Set to `true` to replace existing files.

## Returns

A JSON object:

- `entries` (int) — number of files extracted.
- `destination` (string) — resource key of the destination folder.

## See also

- `package_archive` — the zip counterpart.
- `package_install` — registry-aware install of a published package.
- `packages_overview` — the package folder layout and registry workflow.
